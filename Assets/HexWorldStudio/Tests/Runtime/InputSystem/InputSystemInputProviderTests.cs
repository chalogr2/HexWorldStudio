using HexWorldStudio.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace HexWorldStudio.Tests
{
    /// <summary>
    /// Tests for <see cref="InputSystemInputProvider"/> — drives the
    /// new Input System with a virtual <see cref="Keyboard"/> and
    /// <see cref="Mouse"/> via <see cref="InputTestFixture"/>, asserts
    /// the provider's axes / click frames respond as the legacy
    /// provider would.
    /// </summary>
    /// <remarks>
    /// The whole asmdef this file lives in is define-constrained to
    /// <c>ENABLE_INPUT_SYSTEM</c>, so legacy-only test runs simply
    /// skip the file.
    /// </remarks>
    [TestFixture]
    public class InputSystemInputProviderTests : InputTestFixture
    {
        private GameObject _go;
        private InputSystemInputProvider _provider;
        private Keyboard _keyboard;
        private Mouse _mouse;

        public override void Setup()
        {
            base.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
            // A fresh virtual mouse sits at (0, 0), the bottom-left
            // corner, which edge scroll correctly reads as "pan down-left".
            // Park it mid-screen so the keyboard assertions see only the
            // keys they press.
            Set(_mouse.position, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            _go = new GameObject("InputSystemInputProviderTest");
            _provider = _go.AddComponent<InputSystemInputProvider>();
        }

        public override void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            base.TearDown();
        }

        [Test]
        public void PanAxis_RespondsToWKey()
        {
            Press(_keyboard.wKey);
            Assert.That(_provider.PanAxis.y, Is.GreaterThan(0.5f),
                "Holding W must drive PanAxis.y positive (forward).");
        }

        [Test]
        public void PanAxis_RespondsToSKey()
        {
            Press(_keyboard.sKey);
            Assert.That(_provider.PanAxis.y, Is.LessThan(-0.5f),
                "Holding S must drive PanAxis.y negative.");
        }

        [Test]
        public void PanAxis_RespondsToArrowKeys()
        {
            Press(_keyboard.rightArrowKey);
            Assert.That(_provider.PanAxis.x, Is.GreaterThan(0.5f));
            Release(_keyboard.rightArrowKey);

            Press(_keyboard.leftArrowKey);
            Assert.That(_provider.PanAxis.x, Is.LessThan(-0.5f));
        }

        [Test]
        public void PanAxis_OpposingKeys_CancelOut()
        {
            Press(_keyboard.wKey);
            Press(_keyboard.sKey);
            Assert.That(_provider.PanAxis.y, Is.EqualTo(0f).Within(1e-5f),
                "Holding W and S simultaneously must cancel to zero on Y.");
        }

        [Test]
        public void RotateAxis_RespondsToEKey()
        {
            Press(_keyboard.eKey);
            Assert.That(_provider.RotateAxis, Is.GreaterThan(0.5f),
                "E must drive RotateAxis positive (clockwise from above).");
        }

        [Test]
        public void RotateAxis_RespondsToQKey()
        {
            Press(_keyboard.qKey);
            Assert.That(_provider.RotateAxis, Is.LessThan(-0.5f));
        }

        [Test]
        public void TiltAxis_RespondsToRKey()
        {
            Press(_keyboard.rKey);
            Assert.That(_provider.TiltAxis, Is.GreaterThan(0.5f));
        }

        [Test]
        public void TiltAxis_RespondsToFKey()
        {
            Press(_keyboard.fKey);
            Assert.That(_provider.TiltAxis, Is.LessThan(-0.5f));
        }

        [Test]
        public void PrimaryClickPressed_IsTrueOnlyOnFrameOfPress()
        {
            // Before any press, must be false.
            Assert.That(_provider.PrimaryClickPressed, Is.False, "Before press, PrimaryClickPressed must be false.");

            Press(_mouse.leftButton);
            // On the frame of press, must be true.
            Assert.That(_provider.PrimaryClickPressed, Is.True, "Press frame must surface a true PrimaryClickPressed.");

            // After releasing, must drop back to false.
            Release(_mouse.leftButton);
            Assert.That(_provider.PrimaryClickPressed, Is.False, "Release frame must report false again.");
        }

        [Test]
        public void SecondaryClickPressed_IsTrueOnlyOnFrameOfPress()
        {
            Assert.That(_provider.SecondaryClickPressed, Is.False);
            Press(_mouse.rightButton);
            Assert.That(_provider.SecondaryClickPressed, Is.True);
            Release(_mouse.rightButton);
            Assert.That(_provider.SecondaryClickPressed, Is.False);
        }

        [Test]
        public void NoDevicePresent_ReturnsZeroes_WithoutThrowing()
        {
            // Pull the keyboard out from under the provider and ensure
            // it gracefully returns zero rather than NRE'ing on null devices.
            InputSystem.RemoveDevice(_keyboard);
            InputSystem.RemoveDevice(_mouse);

            Assert.DoesNotThrow(() =>
            {
                Vector2 pan = _provider.PanAxis;
                float zoom = _provider.ZoomDelta;
                float rot = _provider.RotateAxis;
                float tilt = _provider.TiltAxis;
                bool a = _provider.PrimaryClickPressed;
                bool b = _provider.SecondaryClickPressed;
                Assert.That(pan, Is.EqualTo(Vector2.zero));
                Assert.That(zoom, Is.EqualTo(0f));
                Assert.That(rot, Is.EqualTo(0f));
                Assert.That(tilt, Is.EqualTo(0f));
                Assert.That(a, Is.False);
                Assert.That(b, Is.False);
            });
        }
    }
}
