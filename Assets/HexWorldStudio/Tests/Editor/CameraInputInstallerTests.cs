using HexWorldStudio.Input;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// EditMode tests for <see cref="CameraInputInstaller"/> — picks the
    /// right <see cref="ICameraInput"/> implementation at Awake based
    /// on the project's <c>Active Input Handling</c> scripting symbols.
    /// </summary>
    public class CameraInputInstallerTests
    {
        [Test]
        public void Awake_AddsProvider_OfTypeMatchingCurrentSymbols()
        {
            var go = new GameObject("CameraInputInstallerTest");
            try
            {
                CameraInputInstaller installer = go.AddComponent<CameraInputInstaller>();

#if ENABLE_INPUT_SYSTEM
                Assert.That(installer.Installed, Is.Not.Null,
                    "ENABLE_INPUT_SYSTEM is defined — installer must add an ICameraInput.");
                Assert.That(installer.Installed.GetType().Name, Is.EqualTo("InputSystemInputProvider"),
                    "When ENABLE_INPUT_SYSTEM is defined the installer must prefer the new-Input-System provider (ADR-0018).");
#elif ENABLE_LEGACY_INPUT_MANAGER
                Assert.That(installer.Installed, Is.Not.Null,
                    "ENABLE_LEGACY_INPUT_MANAGER is the only symbol set — installer must fall back to the legacy provider.");
                Assert.That(installer.Installed.GetType().Name, Is.EqualTo("LegacyUnityInputProvider"));
#else
                Assert.That(installer.Installed, Is.Null,
                    "With neither input symbol defined the installer must not add a provider.");
#endif
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Awake_DoesNotReplaceExistingProvider_WhenOneIsAlreadyPresent()
        {
            // Pin the buyer-override contract: if a buyer manually
            // attaches an ICameraInput before the installer runs, the
            // installer must not stomp it.
            var go = new GameObject("CameraInputInstallerTest_Override");
            try
            {
                // LegacyUnityInputProvider compiles under any input mode (its body is just stubbed when ENABLE_LEGACY_INPUT_MANAGER isn't defined), so it's a safe stand-in for a buyer-supplied provider in this assertion.
                LegacyUnityInputProvider existing = go.AddComponent<LegacyUnityInputProvider>();
                CameraInputInstaller installer = go.AddComponent<CameraInputInstaller>();

                Assert.That(installer.Installed, Is.SameAs(existing),
                    "Installer must surface the pre-existing provider via Installed rather than adding a sibling.");
                Assert.That(go.GetComponents<ICameraInput>().Length, Is.EqualTo(1),
                    "Installer must not double-attach when a provider is already present.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
