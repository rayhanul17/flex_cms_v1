using FlexCms.Framework.Cms.Comments;
using Xunit;

namespace FlexCms.Tests.Unit.Phase14;

public class CommentSpamFilterTests
{
    [Fact]
    public void Plain_comment_lands_pending()
    {
        var (score, status) = CommentService.ScoreSpam(new FcmsComment { Body = "Great article — thanks!" });
        Assert.True(score < 5);
        Assert.Equal(CommentStatus.Pending, status);
    }

    [Fact]
    public void Six_links_marks_spam()
    {
        var body = string.Join(" ", Enumerable.Range(0, 6).Select(i => $"https://x.com/{i}"));
        var (score, status) = CommentService.ScoreSpam(new FcmsComment { Body = body });
        Assert.True(score >= 5);
        Assert.Equal(CommentStatus.Spam, status);
    }

    [Fact]
    public void Three_links_increments_but_doesnt_auto_spam_alone()
    {
        var body = "check https://a.com and https://b.com and https://c.com";
        var (score, status) = CommentService.ScoreSpam(new FcmsComment { Body = body });
        Assert.True(score >= 2);
        Assert.Equal(CommentStatus.Pending, status);
    }

    [Fact]
    public void Spam_keyword_increments_score()
    {
        var (score, _) = CommentService.ScoreSpam(new FcmsComment { Body = "buy viagra cheap online now lottery winner" });
        Assert.True(score >= 5);
    }

    [Fact]
    public void Excessive_caps_increments_but_doesnt_auto_spam()
    {
        var (score, _) = CommentService.ScoreSpam(new FcmsComment { Body = "GREAT POST EVERYBODY MUST READ THIS NOW URGENT" });
        Assert.True(score >= 2);
    }
}
