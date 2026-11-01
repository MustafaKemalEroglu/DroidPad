using Avalonia.Input;
using WaydroidController.Models;
using WaydroidController.Services;
using KeyBinding = WaydroidController.Models.KeyBinding;

namespace WaydroidController.Managers;

public class KeyInputManager
{
    private readonly KeyMapperEngine _keyMapperEngine = new();
    private readonly HashSet<string> _activePressedKeys = new();
    private KeyBinding? _waitingForKeyBinding;


    public static string NormalizeKeyName(string rawKey)
    {
        if (string.IsNullOrEmpty(rawKey)) return string.Empty;

        if (rawKey.StartsWith("D") && rawKey.Length == 2 && char.IsDigit(rawKey[1]))
        {
            return rawKey[1].ToString();
        }
        if (rawKey.StartsWith("NumPad") && rawKey.Length == 7 && char.IsDigit(rawKey[6]))
        {
            return rawKey[6].ToString();
        }
        return rawKey.ToUpper();
    }

    public void StartWaitingForKey(KeyBinding binding)
    {
        _waitingForKeyBinding = binding;
        binding.KeyName = "...";
        LogService.Info($"⌨️ '{binding.Type}' için yeni tuşa basın...");
    }

    public bool HandleKeyAssignment(KeyEventArgs e)
    {
        if (_waitingForKeyBinding != null)
        {
            var keyStr = e.Key.ToString();
            var normalized = NormalizeKeyName(keyStr);

            _waitingForKeyBinding.KeyName = normalized;
            LogService.Info($"✅ Yeni tuş atandı: {normalized}");
            _waitingForKeyBinding = null;
            return true;
        }
        return false;
    }

    public async Task OnKeyPressedAsync(string keyName, IEnumerable<KeyBinding> keyBindings, IEnumerable<JoystickGroup> joystickGroups, ResolutionDto resolution)
    {
        var normalizedKey = NormalizeKeyName(keyName);
        if (_activePressedKeys.Contains(normalizedKey)) return;
        _activePressedKeys.Add(normalizedKey);

        var tapBinding = keyBindings.FirstOrDefault(b => NormalizeKeyName(b.KeyName).Equals(normalizedKey, StringComparison.OrdinalIgnoreCase));
        if (tapBinding != null)
        {
            tapBinding.IsPressed = true;
            await _keyMapperEngine.HandleKeyPressAsync(normalizedKey, keyBindings.ToList(), resolution);
            return;
        }

        foreach (var jg in joystickGroups)
        {
            bool matched = false;
            if (NormalizeKeyName(jg.UpKey) == normalizedKey) { jg.IsUpPressed = true; matched = true; }
            if (NormalizeKeyName(jg.LeftKey) == normalizedKey) { jg.IsLeftPressed = true; matched = true; }
            if (NormalizeKeyName(jg.DownKey) == normalizedKey) { jg.IsDownPressed = true; matched = true; }
            if (NormalizeKeyName(jg.RightKey) == normalizedKey) { jg.IsRightPressed = true; matched = true; }

            if (matched)
            {
                await _keyMapperEngine.UpdateJoystickGroupStateAsync(jg, resolution);
            }
        }
    }

    public async Task OnKeyReleasedAsync(string keyName, IEnumerable<KeyBinding> keyBindings, IEnumerable<JoystickGroup> joystickGroups, ResolutionDto resolution)
    {
        var normalizedKey = NormalizeKeyName(keyName);
        _activePressedKeys.Remove(normalizedKey);

        var tapBinding = keyBindings.FirstOrDefault(b => NormalizeKeyName(b.KeyName).Equals(normalizedKey, StringComparison.OrdinalIgnoreCase));
        if (tapBinding != null)
        {
            tapBinding.IsPressed = false;
            return;
        }

        foreach (var jg in joystickGroups)
        {
            bool matched = false;
            if (NormalizeKeyName(jg.UpKey) == normalizedKey) { jg.IsUpPressed = false; matched = true; }
            if (NormalizeKeyName(jg.LeftKey) == normalizedKey) { jg.IsLeftPressed = false; matched = true; }
            if (NormalizeKeyName(jg.DownKey) == normalizedKey) { jg.IsDownPressed = false; matched = true; }
            if (NormalizeKeyName(jg.RightKey) == normalizedKey) { jg.IsRightPressed = false; matched = true; }

            if (matched)
            {
                await _keyMapperEngine.UpdateJoystickGroupStateAsync(jg, resolution);
            }
        }
    }
}