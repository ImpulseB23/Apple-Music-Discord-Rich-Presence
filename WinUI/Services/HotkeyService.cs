using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace AppleMusicRpc.Services;

public enum HotkeyAction
{
    ToggleMiniMode,
    PauseResumeRpc
}

public class HotkeyBinding
{
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public bool Win { get; set; }
    public uint Key { get; set; }

    public string DisplayString
    {
        get
        {
            var parts = new List<string>();
            if (Ctrl) parts.Add("Ctrl");
            if (Alt) parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            if (Win) parts.Add("Win");
            parts.Add(GetKeyName(Key));
            return string.Join(" + ", parts);
        }
    }

    private static string GetKeyName(uint key)
    {
        return key switch
        {
            0x4D => "M",
            0x50 => "P",
            0x31 => "1",
            0x32 => "2",
            0x33 => "3",
            0xBC => ",",
            _ => ((char)key).ToString()
        };
    }
}

public class HotkeyService : IDisposable
{
    private static HotkeyService? _instance;
    public static HotkeyService Instance => _instance ??= new HotkeyService();

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    // Virtual key codes
    private const uint VK_M = 0x4D;
    private const uint VK_P = 0x50;

    public const int WM_HOTKEY = 0x0312;

    private IntPtr _hwnd;
    private readonly Dictionary<int, HotkeyAction> _registeredHotkeys = new();
    private readonly Dictionary<HotkeyAction, HotkeyBinding> _bindings = new();
    private int _nextId = 1;

    public event Action<HotkeyAction>? HotkeyPressed;

    private HotkeyService()
    {
        // Default bindings: Ctrl+Alt+M and Ctrl+Alt+P (less common than Ctrl+M/P)
        _bindings[HotkeyAction.ToggleMiniMode] = new HotkeyBinding { Ctrl = true, Alt = true, Key = VK_M };
        _bindings[HotkeyAction.PauseResumeRpc] = new HotkeyBinding { Ctrl = true, Alt = true, Key = VK_P };

        // Load custom bindings from config
        LoadBindings();
    }

    private void LoadBindings()
    {
        var config = ConfigService.Load();
        if (!string.IsNullOrEmpty(config.HotkeyMiniMode))
        {
            var binding = ParseBinding(config.HotkeyMiniMode);
            if (binding != null) _bindings[HotkeyAction.ToggleMiniMode] = binding;
        }
        if (!string.IsNullOrEmpty(config.HotkeyPauseRpc))
        {
            var binding = ParseBinding(config.HotkeyPauseRpc);
            if (binding != null) _bindings[HotkeyAction.PauseResumeRpc] = binding;
        }
    }

    private static HotkeyBinding? ParseBinding(string str)
    {
        if (string.IsNullOrWhiteSpace(str)) return null;

        var binding = new HotkeyBinding();
        var parts = str.ToUpper().Split('+');

        foreach (var part in parts)
        {
            var p = part.Trim();
            switch (p)
            {
                case "CTRL": binding.Ctrl = true; break;
                case "ALT": binding.Alt = true; break;
                case "SHIFT": binding.Shift = true; break;
                case "WIN": binding.Win = true; break;
                default:
                    if (p.Length == 1)
                        binding.Key = (uint)p[0];
                    break;
            }
        }

        return binding.Key != 0 ? binding : null;
    }

    public void Initialize(IntPtr hwnd)
    {
        _hwnd = hwnd;
        RegisterAllHotkeys();
    }

    private void RegisterAllHotkeys()
    {
        foreach (var (action, binding) in _bindings)
        {
            RegisterHotkey(action, binding);
        }
    }

    private void RegisterHotkey(HotkeyAction action, HotkeyBinding binding)
    {
        uint modifiers = MOD_NOREPEAT;
        if (binding.Ctrl) modifiers |= MOD_CONTROL;
        if (binding.Alt) modifiers |= MOD_ALT;
        if (binding.Shift) modifiers |= MOD_SHIFT;
        if (binding.Win) modifiers |= MOD_WIN;

        var id = _nextId++;
        if (RegisterHotKey(_hwnd, id, modifiers, binding.Key))
        {
            _registeredHotkeys[id] = action;
        }
    }

    public void ProcessHotkey(int id)
    {
        if (_registeredHotkeys.TryGetValue(id, out var action))
        {
            HotkeyPressed?.Invoke(action);
        }
    }

    public HotkeyBinding GetBinding(HotkeyAction action)
    {
        return _bindings.TryGetValue(action, out var binding) ? binding : new HotkeyBinding();
    }

    public void UpdateBinding(HotkeyAction action, HotkeyBinding newBinding)
    {
        // Unregister old hotkey
        foreach (var (id, a) in _registeredHotkeys)
        {
            if (a == action)
            {
                UnregisterHotKey(_hwnd, id);
                _registeredHotkeys.Remove(id);
                break;
            }
        }

        // Update and register new
        _bindings[action] = newBinding;
        RegisterHotkey(action, newBinding);

        // Save to config
        var config = ConfigService.Load();
        config.HotkeyMiniMode = _bindings[HotkeyAction.ToggleMiniMode].DisplayString.Replace(" ", "");
        config.HotkeyPauseRpc = _bindings[HotkeyAction.PauseResumeRpc].DisplayString.Replace(" ", "");
        ConfigService.Save(config);
    }

    public void Dispose()
    {
        foreach (var id in _registeredHotkeys.Keys)
        {
            UnregisterHotKey(_hwnd, id);
        }
        _registeredHotkeys.Clear();
    }
}
