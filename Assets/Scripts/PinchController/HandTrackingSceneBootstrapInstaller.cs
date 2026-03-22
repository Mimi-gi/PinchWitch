using System.Reflection;
using Mediapipe.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class HandTrackingSceneBootstrapInstaller
{
  private const string BootstrapObjectName = "Bootstrap";
  private const BindingFlags SerializableFieldFlags =
    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
  // シーン読み込み時の Bootstrap 補完フックを登録する。
  private static void RegisterSceneHook()
  {
    SceneManager.sceneLoaded -= OnSceneLoaded;
    SceneManager.sceneLoaded += OnSceneLoaded;
  }

  // シーン読み込み後に Bootstrap の不足を補完する。
  private static void OnSceneLoaded(Scene scene, LoadSceneMode loadMode)
  {
    if (!scene.IsValid())
    {
      return;
    }

    if (GameObject.Find(BootstrapObjectName) != null)
    {
      return;
    }

    var localBootstrap = FindSceneLocalBootstrap(scene);
    if (localBootstrap == null)
    {
      return;
    }

    CreateBootstrapObject(scene, localBootstrap);
  }

  // 指定名の Bootstrap をシーン内から探す。
  private static Bootstrap FindBootstrapInScene(Scene scene, string objectName)
  {
    foreach (var candidate in Resources.FindObjectsOfTypeAll<Bootstrap>())
    {
      if (candidate == null)
      {
        continue;
      }

      if (candidate.gameObject.scene != scene)
      {
        continue;
      }

      if (candidate.gameObject.name == objectName)
      {
        return candidate;
      }
    }

    return null;
  }

  // シーン内にある任意の Bootstrap を 1 つ取得する。
  private static Bootstrap FindSceneLocalBootstrap(Scene scene)
  {
    foreach (var candidate in Resources.FindObjectsOfTypeAll<Bootstrap>())
    {
      if (candidate == null)
      {
        continue;
      }

      if (candidate.gameObject.scene != scene)
      {
        continue;
      }

      return candidate;
    }

    return null;
  }

  // 既存設定を複製した Bootstrap オブジェクトを生成する。
  private static void CreateBootstrapObject(Scene scene, Bootstrap sourceBootstrap)
  {
    var bootstrapObject = new GameObject(BootstrapObjectName);
    SceneManager.MoveGameObjectToScene(bootstrapObject, scene);
    bootstrapObject.SetActive(false);

    var bootstrap = bootstrapObject.AddComponent<Bootstrap>();
    bootstrap.enabled = false;
    CopySerializedFields(sourceBootstrap, bootstrap);

    CloneImageSourceComponent(sourceBootstrap.GetComponent<WebCamSource>(), bootstrapObject);
    CloneImageSourceComponent(sourceBootstrap.GetComponent<StaticImageSource>(), bootstrapObject);
    CloneImageSourceComponent(sourceBootstrap.GetComponent<VideoSource>(), bootstrapObject);

    bootstrapObject.SetActive(true);
    bootstrap.enabled = true;
  }

  // ImageSource 系コンポーネントを複製先へコピーする。
  private static void CloneImageSourceComponent<T>(T sourceComponent, GameObject bootstrapObject) where T : Behaviour
  {
    if (sourceComponent == null)
    {
      return;
    }

    var clonedComponent = bootstrapObject.AddComponent<T>();
    clonedComponent.enabled = false;
    CopySerializedFields(sourceComponent, clonedComponent);
    clonedComponent.enabled = false;
  }

  // Serialize 対象のフィールドをリフレクションで複製する。
  private static void CopySerializedFields(object source, object destination)
  {
    for (var type = source.GetType(); type != null && type != typeof(Behaviour); type = type.BaseType)
    {
      foreach (var field in type.GetFields(SerializableFieldFlags))
      {
        if (!ShouldCopyField(field))
        {
          continue;
        }

        field.SetValue(destination, field.GetValue(source));
      }
    }
  }

  // フィールドがコピー対象かどうかを判定する。
  private static bool ShouldCopyField(FieldInfo field)
  {
    if (field.IsStatic || field.IsLiteral || field.IsInitOnly || field.IsNotSerialized)
    {
      return false;
    }

    return field.IsPublic || field.IsDefined(typeof(SerializeField), true);
  }
}
