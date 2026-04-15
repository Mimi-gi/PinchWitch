// Copyright (c) 2021 homuler
//
// Use of this source code is governed by an MIT-style
// license that can be found in the LICENSE file or at
// https://opensource.org/licenses/MIT.

using System.Collections;
using System.Reflection;
using UnityEngine;

namespace Mediapipe.Unity
{
  public abstract class Solution : MonoBehaviour
  {
#pragma warning disable IDE1006
    // TODO: make it static
    protected virtual string TAG => GetType().Name;
#pragma warning restore IDE1006

    public Bootstrap bootstrap;
    protected bool isPaused;

    protected virtual IEnumerator Start()
    {
      bootstrap = FindBootstrap();
      yield return new WaitUntil(() => bootstrap.isFinished);

      Play();
    }

    /// <summary>
    ///   Start the main program from the beginning.
    /// </summary>
    public virtual void Play()
    {
      isPaused = false;
    }

    /// <summary>
    ///   Pause the main program.
    /// <summary>
    public virtual void Pause()
    {
      isPaused = true;
    }

    /// <summary>
    ///    Resume the main program.
    ///    If the main program has not begun, it'll do nothing.
    /// </summary>
    public virtual void Resume()
    {
      isPaused = false;
    }

    /// <summary>
    ///   Stops the main program.
    /// </summary>
    public virtual void Stop()
    {
      isPaused = true;
    }

    protected static void SetupAnnotationController<T>(AnnotationController<T> annotationController, ImageSource imageSource, bool expectedToBeMirrored = false) where T : HierarchicalAnnotation
    {
      annotationController.isMirrored = expectedToBeMirrored ^ imageSource.isHorizontallyFlipped ^ imageSource.isFrontFacing;
      annotationController.rotationAngle = imageSource.rotation.Reverse();
    }

    protected static void ReadFromImageSource(ImageSource imageSource, TextureFrame textureFrame)
    {
      var sourceTexture = imageSource.GetCurrentTexture();

      // For some reason, when the image is coiped on GPU, latency tends to be high.
      // So even when OpenGL ES is available, use CPU to copy images.
      var textureType = sourceTexture.GetType();

      if (textureType == typeof(WebCamTexture))
      {
        textureFrame.ReadTextureFromOnCPU((WebCamTexture)sourceTexture);
      }
      else if (textureType == typeof(Texture2D))
      {
        textureFrame.ReadTextureFromOnCPU((Texture2D)sourceTexture);
      }
      else
      {
        textureFrame.ReadTextureFromOnCPU(sourceTexture);
      }
    }

    protected Bootstrap FindBootstrap()
    {
      var bootstrap = PersistentBootstrapManager.TryGetExisting();
      if (bootstrap != null)
      {
        return bootstrap;
      }

      bootstrap = PersistentBootstrapManager.CreateFromTemplate(gameObject);
      if (bootstrap != null)
      {
        Logger.LogWarning(TAG, "Global Bootstrap instance is not found, so creating a persistent Bootstrap from the local scene template");
        DisableMenuButton();
        return bootstrap;
      }

      throw new MissingComponentException($"{TAG} requires a Bootstrap template on the Solution object or an existing global Bootstrap");
    }

    private void DisableMenuButton()
    {
      var menuButton = GameObject.Find("MenuButton");
      if (menuButton != null)
      {
        menuButton.SetActive(false);
      }
    }
  }

  internal static class PersistentBootstrapManager
  {
    private const string BootstrapObjectName = "Bootstrap";
    private const BindingFlags SerializableFieldFlags =
      BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static Bootstrap s_bootstrap;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
      s_bootstrap = null;
    }

    public static Bootstrap TryGetExisting()
    {
      if (s_bootstrap != null)
      {
        return s_bootstrap;
      }

      var bootstrapObj = GameObject.Find(BootstrapObjectName);
      if (bootstrapObj != null)
      {
        s_bootstrap = bootstrapObj.GetComponent<Bootstrap>();
        if (s_bootstrap != null)
        {
          return s_bootstrap;
        }
      }

      foreach (var candidate in Resources.FindObjectsOfTypeAll<Bootstrap>())
      {
        if (candidate == null)
        {
          continue;
        }

        if (candidate.gameObject.name != BootstrapObjectName)
        {
          continue;
        }

        s_bootstrap = candidate;
        return s_bootstrap;
      }

      return null;
    }

    public static Bootstrap CreateFromTemplate(GameObject solutionObject)
    {
      var existing = TryGetExisting();
      if (existing != null)
      {
        return existing;
      }

      if (solutionObject == null)
      {
        return null;
      }

      var sourceBootstrap = solutionObject.GetComponent<Bootstrap>();
      if (sourceBootstrap == null)
      {
        return null;
      }

      var bootstrapObject = new GameObject(BootstrapObjectName);
      bootstrapObject.SetActive(false);
      Object.DontDestroyOnLoad(bootstrapObject);

      var bootstrap = bootstrapObject.AddComponent<Bootstrap>();
      bootstrap.enabled = false;
      CopySerializedFields(sourceBootstrap, bootstrap);

      CloneImageSourceComponent(sourceBootstrap.GetComponent<WebCamSource>(), bootstrapObject);
      CloneImageSourceComponent(sourceBootstrap.GetComponent<StaticImageSource>(), bootstrapObject);
      CloneImageSourceComponent(sourceBootstrap.GetComponent<VideoSource>(), bootstrapObject);

      bootstrapObject.SetActive(true);
      bootstrap.enabled = true;
      s_bootstrap = bootstrap;
      return s_bootstrap;
    }

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

    private static bool ShouldCopyField(FieldInfo field)
    {
      if (field.IsStatic || field.IsLiteral || field.IsInitOnly || field.IsNotSerialized)
      {
        return false;
      }

      return field.IsPublic || field.IsDefined(typeof(SerializeField), true);
    }
  }
}
