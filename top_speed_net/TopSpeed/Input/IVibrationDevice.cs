using System;

namespace TopSpeed.Input
{
    internal interface IVibrationDevice : IDisposable
    {
        bool IsAvailable { get; }
        JoystickStateSnapshot State { get; }
        bool Update();
        
        bool ForceFeedbackCapable { get; }
        void LoadEffect(VibrationEffectType type, string effectPath); // Path optional for XInput
        void PlayEffect(VibrationEffectType type, int intensity = 10000);
        void StopEffect(VibrationEffectType type);
        void Gain(VibrationEffectType type, int value);
    }
}
