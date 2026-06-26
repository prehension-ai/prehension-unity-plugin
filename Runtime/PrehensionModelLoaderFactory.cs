using System;
using UnityEngine;

internal static class PrehensionModelLoaderFactory
{
    private static Func<GameObject, IModelLoader> _override;

    // Runs before BeforeSceneLoad, clearing any override from a previous play session
    // when Enter Play Mode's Reload Domain is disabled.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() => _override = null;

    internal static void Register(Func<GameObject, IModelLoader> factory) => _override = factory;

    internal static IModelLoader Create(GameObject go) =>
        _override != null ? _override(go) : go.AddComponent<PrehensionLibraryLoader>();
}
