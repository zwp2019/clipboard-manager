using ClipboardManager.Application;
using Xunit;

namespace ClipboardManager.Tests;

public class ClipboardDeduplicationServiceTests
{
    [Fact]
    public void SameHash_WithinWindow_IsDuplicate()
    {
        var service = new ClipboardDeduplicationService();
        var now = DateTime.UtcNow;

        Assert.False(service.IsDuplicateWithinWindow("abc", now));
        Assert.True(service.IsDuplicateWithinWindow("abc", now.AddMilliseconds(100)));
    }

    [Fact]
    public void DifferentHash_NotDuplicate()
    {
        var service = new ClipboardDeduplicationService();
        var now = DateTime.UtcNow;

        Assert.False(service.IsDuplicateWithinWindow("abc", now));
        Assert.False(service.IsDuplicateWithinWindow("def", now.AddMilliseconds(100)));
    }

    [Fact]
    public void SameHash_OutsideWindow_NotDuplicate()
    {
        var service = new ClipboardDeduplicationService();
        var now = DateTime.UtcNow;

        Assert.False(service.IsDuplicateWithinWindow("abc", now));
        Assert.False(service.IsDuplicateWithinWindow("abc", now.AddSeconds(10)));
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var service = new ClipboardDeduplicationService();
        var now = DateTime.UtcNow;

        Assert.False(service.IsDuplicateWithinWindow("abc", now));
        service.Reset();
        Assert.False(service.IsDuplicateWithinWindow("abc", now.AddMilliseconds(100)));
    }
}
