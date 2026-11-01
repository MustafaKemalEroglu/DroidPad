using System;
using System.Collections.ObjectModel;
using WaydroidController.Models;
using WaydroidController.Services;

namespace WaydroidController.Managers;

public class KeyMappingManager
{
    public ObservableCollection<KeyBinding> KeyBindings { get; } = new();
    public ObservableCollection<JoystickGroup> JoystickGroups { get; } = new();

    public void AddTapBinding(string key, string coordX, string coordY)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        int targetX = int.TryParse(coordX, out var parsedX) ? parsedX : 500;
        int targetY = int.TryParse(coordY, out var parsedY) ? parsedY : 500;

        KeyBindings.Add(new KeyBinding
        {
            KeyName = key.ToUpper(),
            Type = KeyBindingType.Tap,
            X = targetX,
            Y = targetY
        });

        LogService.Info($"[Tap] Tuş eklendi: {key.ToUpper()} -> ({targetX}, {targetY})");
    }

    public void AddTapAtPosition(Avalonia.Point coord, bool isPanelOpen)
    {
        if (!isPanelOpen) return;

        string keyChar = "F";
        int x = (int)coord.X;
        int y = (int)coord.Y;

        KeyBindings.Add(new KeyBinding
        {
            KeyName = keyChar,
            Type = KeyBindingType.Tap,
            X = x,
            Y = y
        });

        LogService.Info($"[Konum] Yeni Tap tuşu eklendi: {keyChar} -> ({x}, {y})");
    }
    // Çember Joystick Grubu Ekleme
    public void AddJoystickGroup(string preset, int radius, string coordX, string coordY)
    {
        int centerX = int.TryParse(coordX, out var parsedX) ? parsedX : 500;
        int centerY = int.TryParse(coordY, out var parsedY) ? parsedY : 500;

        var (up, left, down, right) = GetPresetKeys(preset);

        JoystickGroups.Add(new JoystickGroup
        {
            X = centerX,
            Y = centerY,
            Radius = radius,
            UpKey = up,
            LeftKey = left,
            DownKey = down,
            RightKey = right,
            PresetName = preset
        });

        LogService.Info($"[Joystick] Çember joystick eklendi ({preset}). Merkez: ({centerX}, {centerY})");
    }

    public void RemoveBinding(KeyBinding? selectedBinding)
    {
        if (selectedBinding is null) return;
        KeyBindings.Remove(selectedBinding);
        LogService.Info("Tuş ataması silindi.");
    }

    public void RemoveJoystick(JoystickGroup? selectedJoystick)
    {
        if (selectedJoystick is null) return;
        JoystickGroups.Remove(selectedJoystick);
        LogService.Info("Joystick grubu silindi.");
    }

    private (string up, string left, string down, string right) GetPresetKeys(string preset)
    {
        return preset switch
        {
            "Yön Tuşları (Arrows)" => ("Up", "Left", "Down", "Right"),
            "Numpad (8462)" => ("NumPad8", "NumPad4", "NumPad2", "NumPad6"),
            _ => ("W", "A", "S", "D")
        };
    }
}