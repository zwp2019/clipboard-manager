using ClipboardManager.Contracts;
using ClipboardManager.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace ClipboardManager.App;

public partial class App
{
    /// <summary>Opt-in isolated visual test data; never reads the user's history or clipboard.</summary>
    private async Task SeedPreviewAsync()
    {
        var repository = _services!.GetRequiredService<IClipboardItemRepository>();
        var workspace = _services!.GetRequiredService<IWorkspaceService>();
        if (await repository.CountAsync(default) > 0) return;
        await workspace.SavePinboardAsync(new Pinboard { Name = "灵感收集", Color = "#E76A8A" }, null, default);
        await workspace.SavePinboardAsync(new Pinboard { Name = "开发片段", Color = "#7C6AF2" }, null, default);
        var examples = new[]
        {
            new ClipboardItem { Type = ClipboardType.Text, Title = "给想法一个落脚点", Text = "每一次复制，都值得被记住。\n\n在这里收集灵感、整理片段，\n随时找回下一次需要的内容。", SourceApplication = "notepad.exe" },
            new ClipboardItem { Type = ClipboardType.Url, Title = "设计参考", Text = "https://learn.microsoft.com/windows/apps/design/", SourceApplication = "msedge.exe", GroupName = "灵感收集" },
            new ClipboardItem { Type = ClipboardType.Code, Title = "一个简单的问候", Text = "public string Hello(string name)\n{\n    return $\"Hello, {name}!\";\n}", SourceApplication = "code.exe", GroupName = "开发片段" },
            new ClipboardItem { Type = ClipboardType.Text, Title = "品牌灵感色", Text = "#8061B7", SourceApplication = "mspaint.exe" },
            new ClipboardItem { Type = ClipboardType.Text, Title = "本周的小目标", Text = "✓ 完成项目界面\n✓ 整理常用片段\n○ 留一点时间给新想法", SourceApplication = "notepad.exe" },
        };
        for (var i = 0; i < examples.Length; i++)
        {
            var item = examples[i]; item.Hash = Guid.NewGuid().ToString("N"); item.Size = (item.Text?.Length ?? 0) * 2;
            item.CreatedAt = DateTime.Now.AddMinutes(-i * 4); item.IsFavorite = i < 2;
            item.Id = await repository.InsertAsync(item, default);
            await repository.UpsertSearchContentAsync(item.Id, item.Text ?? "", default);
        }
    }
}
