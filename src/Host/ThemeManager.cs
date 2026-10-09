using System.Windows;
using System.Windows.Media;

namespace LanCast;

public static class ThemeManager
{
    public sealed class ThemeInfo
    {
        public string Id { get; }
        public string Name { get; }
        public bool Dark { get; }
        public ThemeInfo(string id, string name, bool dark) { Id = id; Name = name; Dark = dark; }
        private ResourceDictionary? _preview;
        private Brush Color(string key) => (Brush)(_preview ??= new ResourceDictionary { Source = new Uri($"pack://application:,,,/LanCast;component/Themes/{Id}.xaml") })[key];
        public Brush Bg => Color("Bg"); public Brush Surface => Color("Surface"); public Brush Sidebar => Color("Sidebar");
        public Brush Accent => Color("Accent"); public Brush Line => Color("Line"); public Brush Text => Color("Text");
        public string Description => Id switch { "Dark" => "O visual original do LanCast", "Light" => "Leve, claro e com alto contraste", "Dracula" => "Roxo vibrante e fundo escuro", "Nord" => "Azuis frios e tons suaves", "Solarized" => "Contraste equilibrado e tons quentes", _ => "Cores marcantes em fundo escuro" };
    }

    public static readonly ThemeInfo[] Themes =
    {
        new("Dark", "Escuro", true),
        new("Light", "Claro", false),
        new("Dracula", "Dracula", true),
        new("Nord", "Nord", true),
        new("Solarized", "Solarized Dark", true),
        new("Monokai", "Monokai", true),
    };

    public static string Current { get; private set; } = "Dark";
    public static bool IsDark => Themes.FirstOrDefault(t => t.Id == Current)?.Dark ?? true;

    public static void Apply(string id)
    {
        if (Themes.All(t => t.Id != id)) id = "Dark";
        Current = id;
        var dict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/LanCast;component/Themes/{id}.xaml") };
        var merged = Application.Current.Resources.MergedDictionaries;
        for (int i = merged.Count - 1; i >= 0; i--)
            if (merged[i].Source?.OriginalString.Contains("Themes/", StringComparison.Ordinal) == true)
                merged.RemoveAt(i);
        merged.Insert(0, dict);
    }
}
