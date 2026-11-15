using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace UToolBar.Models;

public class ClipItem : ObservableObject
{
    private string _text = "";
    private string _kind = "text";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>text 或 image</summary>
    public string Kind
    {
        get => _kind;
        set
        {
            if (Set(ref _kind, value))
            {
                OnPropertyChanged(nameof(IsImage));
                OnPropertyChanged(nameof(TypeText));
                OnPropertyChanged(nameof(PreviewText));
            }
        }
    }

    public string Text
    {
        get => _text;
        set
        {
            if (Set(ref _text, value))
            {
                OnPropertyChanged(nameof(PreviewText));
            }
        }
    }

    /// <summary>图片文件的相对文件名，重启后用来恢复</summary>
    public string ImagePath { get; set; } = "";

    /// <summary>原图宽度（预览用的是缩略图，尺寸单独记）</summary>
    public int ImageWidth { get; set; }

    /// <summary>原图高度</summary>
    public int ImageHeight { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [JsonIgnore]
    public BitmapSource Image { get; set; }

    [JsonIgnore]
    public bool IsImage => Kind == "image";

    [JsonIgnore]
    public string TypeText => IsImage ? "图片" : "文字";

    [JsonIgnore]
    public string TimeText
    {
        get
        {
            var span = DateTime.Now - CreatedAt;
            if (span.TotalMinutes < 1) return "刚刚";
            if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} 分钟前";
            if (span.TotalDays < 1) return $"{(int)span.TotalHours} 小时前";
            return CreatedAt.ToString("M月d日 HH:mm");
        }
    }

    [JsonIgnore]
    public string PreviewText
    {
        get
        {
            if (IsImage)
            {
                int width = ImageWidth > 0 ? ImageWidth : Image?.PixelWidth ?? 0;
                int height = ImageHeight > 0 ? ImageHeight : Image?.PixelHeight ?? 0;
                return width > 0 && height > 0 ? $"{width} × {height} 图片" : "图片";
            }
            var text = (Text ?? "").Replace("\r", "").Replace("\n", " ");
            if (text.Length > 120) text = text[..120] + "…";
            return string.IsNullOrWhiteSpace(text) ? "空白内容" : text;
        }
    }
}
