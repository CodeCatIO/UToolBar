using System.Text.Json.Serialization;

namespace EdgeBar.Models;

public class NoteData : ObservableObject
{
    private string _text = "";
    private string _colorHex = "#FFF3C4";
    private double _opacity = 0.96;
    private bool _isPinned;
    private double _left;
    private double _top;
    private double _width = 264;
    private double _height = 224;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Text
    {
        get => _text;
        set => Set(ref _text, value);
    }

    public string ColorHex
    {
        get => _colorHex;
        set => Set(ref _colorHex, value);
    }

    public double Opacity
    {
        get => _opacity;
        set
        {
            var clamped = Math.Clamp(value, 0.45, 1.0);
            if (Set(ref _opacity, clamped)) OnPropertyChanged(nameof(OpacityPercentText));
        }
    }

    public bool IsPinned
    {
        get => _isPinned;
        set => Set(ref _isPinned, value);
    }

    public double Left
    {
        get => _left;
        set => Set(ref _left, value);
    }

    public double Top
    {
        get => _top;
        set => Set(ref _top, value);
    }

    public double Width
    {
        get => _width;
        set => Set(ref _width, value);
    }

    public double Height
    {
        get => _height;
        set => Set(ref _height, value);
    }

    [JsonIgnore]
    public string OpacityPercentText => $"{Math.Round(Opacity * 100)}%";

    [JsonIgnore]
    public string PreviewTitle
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Text)) return "空白便签";
            var first = Text.Trim().Split('\n')[0].Trim();
            return first.Length > 16 ? first[..16] + "…" : first;
        }
    }
}
