using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

// SDF(Jump Flooding Algorithm)ベースのアウトラインRenderFeature。
// Grabbableレイヤーのオブジェクトをシルエットとして描画し、JFAで距離場を生成、
// グラディエントノイズで輪郭を溶かしながら画面に合成する。
// 表示のオン/オフ・太さは OutlineController が SetWidthMultiplier 経由で制御する。
public class OutlineSDFFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class OutlineSDFFeatureSettings
    {
        public LayerMask targetLayers;
        public float outlineWidthPx = 24f;
        public float jfaMaxRadiusPx = 64f;
        public float noiseScale = 12f;
        public float noiseSpeed = 2f;
        [Range(0f, 1f)] public float noiseWidthStrength = 0.5f;
        // ゆらゆら(炎)用: 揺れの模様が輪郭に沿って流れる速さ。符号で方向が反転する
        public float noiseScrollSpeed = 0.6f;
        [Range(0f, 1f)] public float baseDissolve = 0.234f;
        [Range(0f, 1f)] public float fogOpacity = 0.741f;
        // SDF距離に沿った減衰カーブ。0=減衰なし(均一な帯)、大きいほど根元だけが濃く残る
        [Range(0f, 4f)] public float edgeFalloffPower = 1.5f;
        // 閾値の境界をぼかす幅。帯の太さに対して小さすぎると外縁が硬くシール状に見える
        [Range(0f, 16f)] public float edgeSoftnessPx = 6f;
        // グロウを隠す深度差(ワールド単位)。オブジェクトより手前にシーンが在れば隠す
        [Range(0.001f, 0.5f)] public float depthOcclusionBias = 0.02f;
        // オブジェクト自身に重なる内側の塗りをどれだけ透かすか。0で3Dオブジェクトが完全に手前
        [Range(0f, 1f)] public float interiorOpacity = 0f;
        public float playerFadeStart = 0.1f;
        public float playerFadeEnd = 0.8f;
        [Range(0f, 1f)] public float playerFadeStrength = 0.5f;
    }

    public OutlineSDFFeatureSettings settings = new OutlineSDFFeatureSettings();

    [SerializeField] private Shader _silhouetteShader;
    [SerializeField] private Shader _sdfShader;

    private static float s_widthMultiplier = 1f;
    private static int s_activeOutlineCount;

    // 全体に掛かるマスター太さ倍率(通常1)。個別の太さは Renderer の _OutlineWidth 側で制御する
    public static void SetWidthMultiplier(float value)
    {
        s_widthMultiplier = value;
    }

    // OutlineController が表示中(消えかけ含む)のアウトライン数を通知する。
    // 0の間はシルエット/JFA/合成パスを丸ごとスキップしてGPUコストを0にする。
    public static void SetActiveOutlineCount(int count)
    {
        s_activeOutlineCount = count;
    }

    private OutlineSDFPass _pass;

    public override void Create()
    {
        _pass = new OutlineSDFPass(settings, _silhouetteShader, _sdfShader)
        {
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_silhouetteShader == null || _sdfShader == null)
        {
            return;
        }

        if (renderingData.cameraData.cameraType != CameraType.Game &&
            renderingData.cameraData.cameraType != CameraType.SceneView)
        {
            return;
        }

        // 何もハイライトされていない間はパス自体を積まない
        if (s_activeOutlineCount <= 0)
        {
            return;
        }

        // 対象レイヤーを1つも描画しないカメラではシルエットが空になるだけなので積まない。
        // このプロジェクトはカメラが多く(Stage_10 では8台)、大半は Grabbable レイヤーを
        // 描画しない。それらで合成パスまで走らせると無駄な上に、デバッグ時に
        // 「対象を描かないカメラの出力」が最後に被さって切り分けを誤らせる。
        if ((renderingData.cameraData.camera.cullingMask & settings.targetLayers.value) == 0)
        {
            return;
        }

        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        _pass?.Dispose();
    }

    private class OutlineSDFPass : ScriptableRenderPass
    {
        private const int JfaStepPassIndex = 0;
        private const int CompositePassIndex = 1;

        private static readonly ShaderTagId ForwardTag = new ShaderTagId("UniversalForward");
        private static readonly ShaderTagId ForwardOnlyTag = new ShaderTagId("UniversalForwardOnly");
        private static readonly ShaderTagId UnlitTag = new ShaderTagId("SRPDefaultUnlit");

        private static readonly int WidthMultiplierId = Shader.PropertyToID("_OutlineWidthMultiplier");
        private static readonly int WidthPxId = Shader.PropertyToID("_OutlineWidthPx");
        private static readonly int PlayerWorldPosId = Shader.PropertyToID("_OutlinePlayerWorldPos");
        private static readonly int PlayerFadeStartId = Shader.PropertyToID("_OutlinePlayerFadeStart");
        private static readonly int PlayerFadeEndId = Shader.PropertyToID("_OutlinePlayerFadeEnd");
        private static readonly int PlayerFadeStrengthId = Shader.PropertyToID("_OutlinePlayerFadeStrength");

        private static readonly int SeedTexId = Shader.PropertyToID("_SeedTex");
        private static readonly int PayloadTexId = Shader.PropertyToID("_PayloadTex");
        private static readonly int JfaStepPxId = Shader.PropertyToID("_JFAStepPx");
        private static readonly int NoiseScaleId = Shader.PropertyToID("_NoiseScale");
        private static readonly int NoiseSpeedId = Shader.PropertyToID("_NoiseSpeed");
        private static readonly int NoiseScrollSpeedId = Shader.PropertyToID("_NoiseScrollSpeed");
        private static readonly int NoiseWidthStrengthId = Shader.PropertyToID("_NoiseWidthStrength");
        private static readonly int BaseDissolveId = Shader.PropertyToID("_BaseDissolve");
        private static readonly int FogOpacityId = Shader.PropertyToID("_FogOpacity");
        private static readonly int EdgeFalloffPowerId = Shader.PropertyToID("_EdgeFalloffPower");
        private static readonly int EdgeSoftnessId = Shader.PropertyToID("_EdgeSoftnessPx");
        private static readonly int DepthOcclusionBiasId = Shader.PropertyToID("_DepthOcclusionBias");
        private static readonly int InteriorOpacityId = Shader.PropertyToID("_InteriorOpacity");

        private static readonly Vector2 InvalidSeed = new Vector2(-1e5f, -1e5f);

        private readonly OutlineSDFFeatureSettings _settings;
        private readonly Material _silhouetteMaterial;
        private readonly Material _sdfMaterial;

        private Transform _playerTransform;
        private bool _playerSearched;

        public OutlineSDFPass(OutlineSDFFeatureSettings settings, Shader silhouetteShader, Shader sdfShader)
        {
            _settings = settings;
            _silhouetteMaterial = silhouetteShader != null ? CoreUtils.CreateEngineMaterial(silhouetteShader) : null;
            _sdfMaterial = sdfShader != null ? CoreUtils.CreateEngineMaterial(sdfShader) : null;

            // 合成時に「オブジェクトより手前のシーン」を判定するため _CameraDepthTexture を要求する
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        public void Dispose()
        {
            CoreUtils.Destroy(_silhouetteMaterial);
            CoreUtils.Destroy(_sdfMaterial);
        }

        private Vector3 GetPlayerWorldPos()
        {
            if (_playerTransform == null && !_playerSearched)
            {
                _playerSearched = true;
                var playerObj = GameObject.FindWithTag("Player");
                if (playerObj != null)
                {
                    _playerTransform = playerObj.transform;
                }
            }

            return _playerTransform != null ? _playerTransform.position : new Vector3(1e6f, 1e6f, 1e6f);
        }

        private class MaskPassData
        {
            public RendererListHandle rendererList;
        }

        private class BlitPassData
        {
            public TextureHandle seedSrc;
            public TextureHandle payloadSrc;
            public Material material;
            public int pass;
            public float jfaStepPx;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_silhouetteMaterial == null || _sdfMaterial == null)
            {
                return;
            }

            var resourceData = frameData.Get<UniversalResourceData>();
            var cameraData = frameData.Get<UniversalCameraData>();
            var renderingData = frameData.Get<UniversalRenderingData>();

            if (!resourceData.cameraColor.IsValid())
            {
                return;
            }

            var cameraDesc = renderGraph.GetTextureDesc(resourceData.cameraColor);
            int width = cameraDesc.width;
            int height = cameraDesc.height;

            // xy = シード座標(スクリーンピクセル)。
            // payload(R16G16B16A16 = 64bpp)と MRT を組むので、ここも 64bpp に揃える。
            // ビット幅が食い違うと Native Render Pass が成立せず描画が丸ごと落ちる。
            var seedDesc = new TextureDesc(width, height)
            {
                colorFormat = GraphicsFormat.R32G32_SFloat,
                clearBuffer = true,
                clearColor = new Color(InvalidSeed.x, InvalidSeed.y, 0f, 0f),
                filterMode = FilterMode.Point,
                name = "_OutlineSeedA"
            };
            TextureHandle seedA = renderGraph.CreateTexture(seedDesc);
            seedDesc.name = "_OutlineSeedB";
            TextureHandle seedB = renderGraph.CreateTexture(seedDesc);

            var payloadDesc = new TextureDesc(width, height)
            {
                colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                clearBuffer = true,
                clearColor = Color.clear,
                filterMode = FilterMode.Point,
                name = "_OutlinePayloadA"
            };
            TextureHandle payloadA = renderGraph.CreateTexture(payloadDesc);
            payloadDesc.name = "_OutlinePayloadB";
            TextureHandle payloadB = renderGraph.CreateTexture(payloadDesc);

            // ---- シルエット(マスク)パス ----
            using (var builder = renderGraph.AddRasterRenderPass<MaskPassData>("Outline Silhouette", out var passData))
            {
                _silhouetteMaterial.SetFloat(WidthMultiplierId, s_widthMultiplier);
                _silhouetteMaterial.SetFloat(WidthPxId, _settings.outlineWidthPx);
                _silhouetteMaterial.SetVector(PlayerWorldPosId, GetPlayerWorldPos());
                _silhouetteMaterial.SetFloat(PlayerFadeStartId, _settings.playerFadeStart);
                _silhouetteMaterial.SetFloat(PlayerFadeEndId, _settings.playerFadeEnd);
                _silhouetteMaterial.SetFloat(PlayerFadeStrengthId, _settings.playerFadeStrength);

                var lightData = frameData.Get<UniversalLightData>();
                var drawSettings = RenderingUtils.CreateDrawingSettings(
                    ForwardTag, renderingData, cameraData, lightData, SortingCriteria.CommonOpaque);
                drawSettings.SetShaderPassName(1, ForwardOnlyTag);
                drawSettings.SetShaderPassName(2, UnlitTag);
                drawSettings.overrideMaterial = _silhouetteMaterial;
                drawSettings.overrideMaterialPassIndex = 0;

                var filterSettings = new FilteringSettings(RenderQueueRange.all, _settings.targetLayers);
                var listParams = new RendererListParams(renderingData.cullResults, drawSettings, filterSettings);
                passData.rendererList = renderGraph.CreateRendererList(listParams);

                builder.UseRendererList(passData.rendererList);
                builder.SetRenderAttachment(seedA, 0);
                builder.SetRenderAttachment(payloadA, 1);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((MaskPassData data, RasterGraphContext ctx) =>
                {
                    ctx.cmd.DrawRendererList(data.rendererList);
                });
            }

            // ---- JFA (Jump Flooding Algorithm) ----
            int maxDim = Mathf.Max(width, height);
            int startStep = Mathf.NextPowerOfTwo(Mathf.Clamp((int)_settings.jfaMaxRadiusPx, 1, maxDim));

            TextureHandle curSeed = seedA;
            TextureHandle curPayload = payloadA;
            TextureHandle nextSeed = seedB;
            TextureHandle nextPayload = payloadB;

            for (int step = startStep; step >= 1; step /= 2)
            {
                float stepPx = step;
                using (var builder = renderGraph.AddRasterRenderPass<BlitPassData>($"Outline JFA Step {step}", out var passData))
                {
                    passData.seedSrc = curSeed;
                    passData.payloadSrc = curPayload;
                    passData.material = _sdfMaterial;
                    passData.pass = JfaStepPassIndex;
                    passData.jfaStepPx = stepPx;

                    builder.UseTexture(curSeed);
                    builder.UseTexture(curPayload);
                    builder.SetRenderAttachment(nextSeed, 0);
                    builder.SetRenderAttachment(nextPayload, 1);

                    // Material.SetTexture はコマンドバッファに値を焼き込まないため、
                    // 全パスで共有している _sdfMaterial では最後の代入が全パスに適用されてしまう。
                    // cmd.SetGlobal* は記録位置で値が確定するので、パスごとに正しい入力を渡せる。
                    builder.AllowGlobalStateModification(true);
                    builder.SetRenderFunc((BlitPassData data, RasterGraphContext ctx) =>
                    {
                        ctx.cmd.SetGlobalTexture(SeedTexId, data.seedSrc);
                        ctx.cmd.SetGlobalTexture(PayloadTexId, data.payloadSrc);
                        ctx.cmd.SetGlobalFloat(JfaStepPxId, data.jfaStepPx);
                        Blitter.BlitTexture(ctx.cmd, data.seedSrc, new Vector4(1, 1, 0, 0), data.material, data.pass);
                    });
                }

                (curSeed, nextSeed) = (nextSeed, curSeed);
                (curPayload, nextPayload) = (nextPayload, curPayload);
            }

            // ---- 画面合成 ----
            using (var builder = renderGraph.AddRasterRenderPass<BlitPassData>("Outline Composite", out var passData))
            {
                passData.seedSrc = curSeed;
                passData.payloadSrc = curPayload;
                passData.material = _sdfMaterial;
                passData.pass = CompositePassIndex;

                _sdfMaterial.SetFloat(NoiseScaleId, _settings.noiseScale);
                _sdfMaterial.SetFloat(NoiseSpeedId, _settings.noiseSpeed);
                _sdfMaterial.SetFloat(NoiseScrollSpeedId, _settings.noiseScrollSpeed);
                _sdfMaterial.SetFloat(NoiseWidthStrengthId, _settings.noiseWidthStrength);
                _sdfMaterial.SetFloat(BaseDissolveId, _settings.baseDissolve);
                _sdfMaterial.SetFloat(FogOpacityId, _settings.fogOpacity);
                _sdfMaterial.SetFloat(EdgeFalloffPowerId, _settings.edgeFalloffPower);
                _sdfMaterial.SetFloat(EdgeSoftnessId, _settings.edgeSoftnessPx);
                _sdfMaterial.SetFloat(DepthOcclusionBiasId, _settings.depthOcclusionBias);
                _sdfMaterial.SetFloat(InteriorOpacityId, _settings.interiorOpacity);

                builder.UseTexture(curSeed);
                builder.UseTexture(curPayload);

                // グローバルの _CameraDepthTexture をシェーダーで読むため依存を明示する
                if (resourceData.cameraDepthTexture.IsValid())
                {
                    builder.UseTexture(resourceData.cameraDepthTexture);
                }

                builder.SetRenderAttachment(resourceData.cameraColor, 0);
                builder.AllowGlobalStateModification(true);
                builder.SetRenderFunc((BlitPassData data, RasterGraphContext ctx) =>
                {
                    ctx.cmd.SetGlobalTexture(SeedTexId, data.seedSrc);
                    ctx.cmd.SetGlobalTexture(PayloadTexId, data.payloadSrc);
                    Blitter.BlitTexture(ctx.cmd, data.seedSrc, new Vector4(1, 1, 0, 0), data.material, data.pass);
                });
            }
        }
    }
}
