using ClipboardManager.App.ViewModels;
using ClipboardManager.Domain;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ClipboardManager.App.Controls;

public sealed partial class ItemDetails : UserControl, IDisposable
{
    private readonly LauncherViewModel _owner;
    private readonly ClipboardItemViewModel? _item;
    private CancellationTokenSource? _imageCts;
    private CancellationTokenSource? _ocrCts;
    public string ItemTitle => TitleBox.Text;
    public string ItemText => ContentBox.Text;

    public ItemDetails(LauncherViewModel owner, ClipboardItemViewModel? item, bool edit)
    {
        InitializeComponent();
        _owner = owner; _item = item;
        Unloaded += (_, _) => Dispose();
        TitleBox.Text = item?.Item.Title ?? string.Empty;
        ContentBox.Text = item?.Item.Text ?? item?.Item.FilePaths ?? item?.Item.OcrText ?? string.Empty;
        TitleBox.IsReadOnly = !edit;
        ContentBox.IsReadOnly = !edit || item?.Item.Type is ClipboardType.Image or ClipboardType.File;
        Metadata.Text = item is null ? "新建的文本会自动加入收藏，方便长期复用。" :
            $"{item.TypeLabel} · {item.SourceName ?? "本地"} · {item.Item.CreatedAt:yyyy-MM-dd HH:mm} · {item.Item.Size:N0} 字节";
        HintText.Text = item?.Item.Type is ClipboardType.Html or ClipboardType.Rtf
            ? "只修改名称会保留格式；修改正文后保存为纯文本。" : "Ctrl+A 全选正文，Ctrl+C 复制。";
        if (item?.IsImage == true && File.Exists(item.Item.ImagePath))
        {
            OriginalImage.Visibility = Visibility.Visible;
            OcrButton.Visibility = Visibility.Visible;
            OcrOptions.Visibility = Visibility.Visible;
            OcrLanguageBox.Items.Add(new ComboBoxItem { Content = "自动（系统语言，英文自动复核）", Tag = null });
            foreach (var language in Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages)
                OcrLanguageBox.Items.Add(new ComboBoxItem { Content = language.NativeName, Tag = language.LanguageTag });
            OcrLanguageHint.Text = Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages.Any(l => l.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                ? "英文截图可选 English；小字号可尝试开启小字增强。"
                : "当前未安装英文 OCR 模型。识别英文截图建议在 Windows 语言设置中安装 English (United States) 的文字识别组件。";
            OcrLanguageBox.SelectedIndex = 0;
            Loaded += LoadOriginalImage;
        }
    }

    private async void LoadOriginalImage(object sender, RoutedEventArgs e)
    {
        _imageCts?.Cancel(); _imageCts?.Dispose(); _imageCts = new CancellationTokenSource();
        try
        {
            await using var stream = new FileStream(_item!.Item.ImagePath!, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true);
            var bitmap = new BitmapImage { DecodePixelWidth = 1280 };
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream()).AsTask(_imageCts.Token);
            OriginalImage.Source = bitmap;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _owner.ReportError(ex); HintText.Text = "图片无法读取，原文件可能已移动或损坏。"; }
    }
    public void Dispose()
    {
        _imageCts?.Cancel(); _imageCts?.Dispose(); _imageCts = null;
        _ocrCts?.Cancel(); _ocrCts?.Dispose(); _ocrCts = null;
        GC.SuppressFinalize(this);
    }

    private async void Ocr_Click(object sender, RoutedEventArgs e)
    {
        if (_item is null) return;
        OcrButton.IsEnabled = false;
        OcrLanguageBox.IsEnabled = false;
        EnhanceSmallText.IsEnabled = false;
        OcrProgress.Visibility = Visibility.Visible;
        OcrProgress.IsActive = true;
        HintText.Text = "正在本地识别，长图会分块处理…";
        _ocrCts?.Dispose(); _ocrCts = new CancellationTokenSource();
        try
        {
            var language = (OcrLanguageBox.SelectedItem as ComboBoxItem)?.Tag as string;
            var text = await _owner.ExtractTextAsync(_item, _ocrCts.Token, language, EnhanceSmallText.IsChecked == true);
            if (!string.IsNullOrWhiteSpace(text)) ContentBox.Text = text;
            HintText.Text = string.IsNullOrWhiteSpace(text) ? "未识别到文字，可更换语言或开启小字增强；原结果已保留。" : "识别完成，已建立搜索索引。若有错字，可更换识别语言后重试。";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _owner.ReportError(ex); HintText.Text = $"识别失败：{ex.Message}"; }
        finally
        {
            OcrButton.IsEnabled = true; OcrLanguageBox.IsEnabled = true; EnhanceSmallText.IsEnabled = true;
            OcrProgress.IsActive = false; OcrProgress.Visibility = Visibility.Collapsed;
        }
    }
}
