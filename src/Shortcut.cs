using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Pegline
{
    /// A global key combination, kept in settings as text like "Ctrl+Alt+S".
    class Shortcut
    {
        public readonly uint Mods;
        public readonly uint Vk;

        /// Ctrl+Alt+S: clear of QQ (Ctrl+Alt+A), WeChat (Alt+A), DingTalk
        /// and Feishu (Ctrl+Shift+A), so starting at login steals none of them.
        public static readonly Shortcut DefaultSnip = new Shortcut(Native.MOD_CONTROL | Native.MOD_ALT, 'S');
        public static readonly Shortcut Toggle = new Shortcut(Native.MOD_CONTROL | Native.MOD_ALT, 'T');

        public Shortcut(uint mods, uint vk)
        {
            Mods = mods;
            Vk = vk;
        }

        /// Function keys and PrtScn work on their own; anything else needs a
        /// modifier, or typing that letter anywhere would take a screenshot.
        public bool Usable
        {
            get
            {
                bool alone = (Vk >= 0x70 && Vk <= 0x87) || Vk == 0x2C;
                return Vk != 0 && (Mods != 0 || alone);
            }
        }

        public override bool Equals(object o)
        {
            var s = o as Shortcut;
            return s != null && s.Mods == Mods && s.Vk == Vk;
        }

        public override int GetHashCode() { return (int)(Mods * 397 ^ Vk); }

        public override string ToString()
        {
            string text = "";
            if ((Mods & Native.MOD_CONTROL) != 0) text += "Ctrl+";
            if ((Mods & Native.MOD_ALT) != 0) text += "Alt+";
            if ((Mods & Native.MOD_SHIFT) != 0) text += "Shift+";
            if ((Mods & Native.MOD_WIN) != 0) text += "Win+";
            return text + KeyName(Vk);
        }

        static string KeyName(uint vk)
        {
            if ((vk >= 'A' && vk <= 'Z') || (vk >= '0' && vk <= '9')) return ((char)vk).ToString();
            if (vk == 0x2C) return "PrtScn";
            return KeyInterop.KeyFromVirtualKey((int)vk).ToString();
        }

        /// Reads what ToString wrote; null for "none" or anything unreadable.
        public static Shortcut Parse(string text)
        {
            if (string.IsNullOrEmpty(text) || text == "none") return null;
            var parts = text.Split('+');
            uint mods = 0;
            foreach (var p in parts.Take(parts.Length - 1))
            {
                if (p == "Ctrl") mods |= Native.MOD_CONTROL;
                else if (p == "Alt") mods |= Native.MOD_ALT;
                else if (p == "Shift") mods |= Native.MOD_SHIFT;
                else if (p == "Win") mods |= Native.MOD_WIN;
                else return null;
            }
            string k = parts[parts.Length - 1];
            uint vk;
            Key key;
            if (k.Length == 1 && char.IsLetterOrDigit(k[0])) vk = char.ToUpperInvariant(k[0]);
            else if (k == "PrtScn") vk = 0x2C;
            else if (Enum.TryParse(k, out key)) vk = (uint)KeyInterop.VirtualKeyFromKey(key);
            else return null;
            var s = new Shortcut(mods, vk);
            return s.Usable ? s : null;
        }
    }

    /// Asks for a new combination by having you press it.
    /// Fluent-styled to look like a WinUI 3 dialog: Segoe UI Variable type,
    /// 4px rounded buttons, and the WinUI 3 light-theme palette. The key
    /// capture behavior is unchanged: buttons never take focus and the
    /// input method stays off so every key press reaches the window.
    class ShortcutDialog : Window
    {
        readonly Func<Shortcut, string> apply;
        readonly TextBlock combo = new TextBlock();
        readonly TextBlock error = new TextBlock();
        readonly Button ok;
        Shortcut pressed;

        /// Whether a new choice (a shortcut, or none) was applied.
        public bool Applied;

        /// apply tries a choice (null for none) and returns why it cannot be
        /// used, or null once it is in place.
        public ShortcutDialog(Shortcut current, Func<Shortcut, string> apply)
        {
            this.apply = apply;
            Title = Loc.L("Screenshot shortcut", "Atajo de captura", "截图快捷键");
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;
            ShowInTaskbar = true;
            // WinUI 3 dialog surface.
            Background = new SolidColorBrush(Color.FromRgb(0xF9, 0xF9, 0xF9));
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            // Keys go straight to the dialog: a Chinese or Japanese input
            // method would otherwise swallow a lone letter into a composition.
            InputMethod.SetIsInputMethodEnabled(this, false);

            var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20), Width = 380 };
            panel.Children.Add(new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1B)),
                Text = Loc.L("Press the keys you want to use. Include Ctrl, Alt, Shift or Win, or use F1–F24 or PrtScn on their own.",
                             "Pulsa las teclas que quieras usar. Incluye Ctrl, Alt, Mayús o Win, o usa F1–F24 o ImprPant solas.",
                             "直接按下想用的组合键。需要包含 Ctrl、Alt、Shift 或 Win；F1–F24、PrtScn 可以单独使用。")
            });
            combo.FontSize = 24;
            combo.FontWeight = FontWeights.SemiBold;
            combo.HorizontalAlignment = HorizontalAlignment.Center;
            combo.Foreground = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x1B));
            combo.Text = current != null ? current.ToString() : Loc.L("None", "Ninguno", "未设置");
            panel.Children.Add(new Border
            {
                Margin = new Thickness(0, 12, 0, 4), Padding = new Thickness(12), CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Color.FromRgb(0xE1, 0xE1, 0xE1)),
                Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)), Child = combo
            });
            error.Foreground = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));
            error.FontSize = 12;
            error.TextWrapping = TextWrapping.Wrap;
            error.MinHeight = 18;
            panel.Children.Add(error);

            Style plain = FluentButtonStyle(false);
            Style accent = FluentButtonStyle(true);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            // The buttons never take focus, so every key press reaches the window.
            Func<string, Action, Style, Button> button = (text, act, style) =>
            {
                var b = new Button { Content = text, Margin = new Thickness(8, 0, 0, 0), Focusable = false, Style = style };
                b.Click += delegate { act(); };
                buttons.Children.Add(b);
                return b;
            };
            button(Loc.L("No shortcut", "Sin atajo", "不使用快捷键"), () => Choose(null), plain);
            button(Loc.L("Cancel", "Cancelar", "取消"), Close, plain);
            ok = button(Loc.L("OK", "Aceptar", "确定"), () => { if (pressed != null) Choose(pressed); }, accent);
            ok.IsEnabled = false;
            panel.Children.Add(buttons);
            Content = panel;

            PreviewKeyDown += (s, e) => { e.Handled = true; Pressed(e, false); };
            PreviewKeyUp += (s, e) =>
            {
                // Windows only reports PrtScn when it is released.
                Key k = e.Key == Key.System ? e.SystemKey : e.Key;
                if (k == Key.Snapshot) { e.Handled = true; Pressed(e, true); }
            };
            Loaded += delegate { Activate(); Focus(); };
        }

        /// A WinUI 3 style button built in code: 4px rounded corners, 1px
        /// stroke, 32px tall, with hover / pressed / disabled states.
        /// accent=true renders the blue primary button (used for OK).
        static Style FluentButtonStyle(bool accent)
        {
            // WinUI 3 light-theme palette.
            Color bg     = accent ? Color.FromRgb(0x00, 0x67, 0xC0) : Color.FromRgb(0xFF, 0xFF, 0xFF);
            Color stroke = accent ? Color.FromRgb(0x00, 0x67, 0xC0) : Color.FromRgb(0xE1, 0xE1, 0xE1);
            Color fg     = accent ? Color.FromRgb(0xFF, 0xFF, 0xFF) : Color.FromRgb(0x1B, 0x1B, 0x1B);
            Color hoverBg     = accent ? Color.FromRgb(0x19, 0x72, 0xC2) : Color.FromRgb(0xF9, 0xF9, 0xF9);
            Color hoverStroke = accent ? Color.FromRgb(0x19, 0x72, 0xC2) : Color.FromRgb(0xD1, 0xD1, 0xD1);
            Color downBg     = accent ? Color.FromRgb(0x00, 0x58, 0xA9) : Color.FromRgb(0xF5, 0xF5, 0xF5);
            Color downStroke = accent ? Color.FromRgb(0x00, 0x58, 0xA9) : Color.FromRgb(0xC7, 0xC7, 0xC7);

            var style = new Style(typeof(Button));

            // Rounded frame that follows whatever the triggers set on the
            // button itself, through templated-parent bindings.
            var frame = new FrameworkElementFactory(typeof(Border), "frame");
            frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            frame.SetBinding(Border.BackgroundProperty, TemplatedParentBinding(Button.BackgroundProperty));
            frame.SetBinding(Border.BorderBrushProperty, TemplatedParentBinding(Button.BorderBrushProperty));
            frame.SetBinding(Border.BorderThicknessProperty, TemplatedParentBinding(Button.BorderThicknessProperty));
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 0, 12, 0));
            frame.AppendChild(content);
            style.Setters.Add(new Setter(Button.TemplateProperty, new ControlTemplate(typeof(Button)) { VisualTree = frame }));

            style.Setters.Add(new Setter(Button.BackgroundProperty, new SolidColorBrush(bg)));
            style.Setters.Add(new Setter(Button.BorderBrushProperty, new SolidColorBrush(stroke)));
            style.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Button.ForegroundProperty, new SolidColorBrush(fg)));
            style.Setters.Add(new Setter(Button.HeightProperty, 32.0));
            style.Setters.Add(new Setter(Button.MinWidthProperty, 96.0));
            style.Setters.Add(new Setter(Button.FontSizeProperty, 14.0));

            // Later triggers win, so disabled comes last.
            style.Triggers.Add(StateTrigger(Button.IsMouseOverProperty,
                new Setter(Button.BackgroundProperty, new SolidColorBrush(hoverBg)),
                new Setter(Button.BorderBrushProperty, new SolidColorBrush(hoverStroke))));
            style.Triggers.Add(StateTrigger(Button.IsPressedProperty,
                new Setter(Button.BackgroundProperty, new SolidColorBrush(downBg)),
                new Setter(Button.BorderBrushProperty, new SolidColorBrush(downStroke))));
            var disabled = new Trigger { Property = Button.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(Button.BackgroundProperty, new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5))));
            disabled.Setters.Add(new Setter(Button.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0xE1, 0xE1, 0xE1))));
            disabled.Setters.Add(new Setter(Button.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0x9D, 0x9D, 0x9D))));
            style.Triggers.Add(disabled);

            return style;
        }

        static Trigger StateTrigger(DependencyProperty property, params Setter[] setters)
        {
            var trigger = new Trigger { Property = property, Value = true };
            foreach (var s in setters) trigger.Setters.Add(s);
            return trigger;
        }

        static Binding TemplatedParentBinding(DependencyProperty property)
        {
            return new Binding(property.Name) { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) };
        }

        void Pressed(KeyEventArgs e, bool released)
        {
            Key key = e.Key == Key.System ? e.SystemKey : e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
            if (key == Key.Snapshot && !released) return;
            uint mods = 0;
            var m = Keyboard.Modifiers;
            if ((m & ModifierKeys.Control) != 0) mods |= Native.MOD_CONTROL;
            if ((m & ModifierKeys.Alt) != 0) mods |= Native.MOD_ALT;
            if ((m & ModifierKeys.Shift) != 0) mods |= Native.MOD_SHIFT;
            if (Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) mods |= Native.MOD_WIN;

            if (mods == 0 && key == Key.Escape) { Close(); return; }
            if (mods == 0 && key == Key.Enter) { if (pressed != null) Choose(pressed); return; }

            bool modifierOnly = key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt
                || key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin;
            if (modifierOnly)
            {
                // Show the modifiers as they go down, before the key itself.
                combo.Text = new Shortcut(mods, 0).ToString().Replace("None", "…");
                return;
            }
            var s = new Shortcut(mods, (uint)KeyInterop.VirtualKeyFromKey(key));
            combo.Text = s.ToString();
            if (!s.Usable)
            {
                pressed = null;
                ok.IsEnabled = false;
                error.Text = Loc.L("Add Ctrl, Alt, Shift or Win.", "Añade Ctrl, Alt, Mayús o Win.", "请加上 Ctrl、Alt、Shift 或 Win。");
                return;
            }
            pressed = s;
            ok.IsEnabled = true;
            error.Text = "";
        }

        void Choose(Shortcut s)
        {
            string why = apply(s);
            if (why != null) { error.Text = why; return; }
            Applied = true;
            Close();
        }
    }
}
