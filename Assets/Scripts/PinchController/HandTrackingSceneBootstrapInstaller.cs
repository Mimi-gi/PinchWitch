using System.Reflection;
using Mediapipe.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class HandTrackingSceneBootstrapInstaller
{
  private const string TargetSceneName = "HandTrackingScene";
  private const string BootstrapObjectName = "Bootstrap";
  private const BindingFlags SerializableFieldFlags =
    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

  [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
  private static void RegisterSceneHook()
  {
    SceneManager.sceneLoaded -= OnSceneLoaded;
    SceneManager.sceneLoaded += OnSceneLoaded;
  }

  private static void OnSceneLoaded(Scene scene, LoadSceneMode loadMode)
  {
    if (!scene.IsValid() || scene.name != TargetSceneName)
    {
      return;
    }

    if (FindBootstrapInScene(scene, BootstrapObjectName) != null)
    {
      return;
    }

    var localBootstrap = FindSceneLocalBootstrap(scene);
    if (localBootstrap == null)
    {
      Debug.LogWarning($"[{nameof(HandTrackingSceneBootstrapInstaller)}] Scene-local Bootstrap source was not found.");
      return;
    }

    CreateBootstrapObject(scene, localBootstrap);
  }

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
