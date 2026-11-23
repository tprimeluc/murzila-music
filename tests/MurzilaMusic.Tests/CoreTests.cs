using System.IO;
using MurzilaMusic.App.Models;
using MurzilaMusic.App.Services;
using Xunit;

namespace MurzilaMusic.Tests;

public class CoreTests
{
    [Fact]
    public void WordSyncLrc_Parses_Each_Word_Timestamp()
    {
        const string lrc = "[00:01.00]<00:01.00>Hello <00:01.50>world\n" +
                           "[00:03.00]<00:03.00>again";

        var doc = LyricsService.Parse(lrc);

        Assert.NotNull(doc);
        Assert.True(doc.HasTimestamps);
        Assert.Equal(2, doc.Lines.Count);
        Assert.Equal(2, doc.Lines[0].Words.Count);
        Assert.Equal("Hello", doc.Lines[0].Words[0].Text);
        Assert.Equal(1.0, doc.Lines[0].Words[0].Start, 2);
        Assert.Equal(1.5, doc.Lines[0].Words[1].Start, 2);
        Assert.Equal("world", doc.Lines[0].RawText.Split(' ')[1].Trim());
    }

    [Fact]
    public void Enhanced_Lrc_Creates_Continuous_Letter_Windows()
    {
        const string lrc = "[00:01.00]<00:01.00>Hi <00:01.40>there\n" +
                           "[00:03.00]<00:03.00>next";

        var doc = LyricsService.Parse(lrc);

        Assert.NotNull(doc);
        Assert.Equal(doc.Lines[0].Words[1].Start, doc.Lines[0].Words[0].End, 2);
        Assert.True(doc.Lines[0].Words[1].End > doc.Lines[0].Words[1].Start);
        Assert.All(doc.Lines[0].Words, w => Assert.True(w.End >= w.Start));
    }

    [Fact]
    public void LineOnly_Lrc_Distributes_Words()
    {
        var doc = LyricsService.Parse("[00:00.00]one two three\n[00:02.00]four");

        Assert.NotNull(doc);
        Assert.Equal(3, doc.Lines[0].Words.Count);
        Assert.Equal("one", doc.Lines[0].Words[0].Text);
        Assert.All(doc.Lines[0].Words, w => Assert.True(w.End > w.Start));
    }

    [Fact]
    public void Local_Lyrics_Catalog_Finds_Lrc_By_Artist_And_Title()
    {
        AppPaths.Ensure();
        string path = Path.Combine(AppPaths.Lyrics, "TestArtist - TestTitle.lrc");
        File.WriteAllText(path, "[ar:TestArtist]\n[ti:TestTitle]\n[00:00.00]local line one\n[00:02.00]local line two\n");

        try
        {
            var catalog = LocalLyricsCatalog.Default;
            catalog.Refresh();
            var doc = catalog.Find(new TrackItem
            {
                Title = "TestTitle",
                Artist = "TestArtist",
                FileName = @"C:\some\where\unknown.mp3"
            });

            Assert.NotNull(doc);
            Assert.Equal(2, doc!.Lines.Count);
            Assert.Equal("Локальная папка текстов", doc.Source);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            LocalLyricsCatalog.Default.Refresh();
        }
    }

    [Fact]
    public void Free_Allows_Short_Tracks_Without_Login()
    {
        var track = new TrackItem { DurationSeconds = 200 };
        var result = AccessPolicy.Check(track, false, new LibraryData(), new AppSettings());

        Assert.True(result.Allowed);
        Assert.False(result.NeedsLogin);
    }

    [Fact]
    public void Long_Track_Requires_Login_And_Pro_Plan()
    {
        var track = new TrackItem { DurationSeconds = 420 }; // 7 минут — больше лимита Free (6)
        var result = AccessPolicy.Check(track, false, new LibraryData(), new AppSettings());

        Assert.False(result.Allowed);
        Assert.True(result.NeedsLogin);
        Assert.True(result.NeedsPayment);
        Assert.Equal(50, result.AmountRub);
        Assert.Equal(Tariff.Pro50, result.RequiredTariff);
    }

    [Fact]
    public void Pro_50_Allows_Ten_Minute_Track()
    {
        var track = new TrackItem { DurationSeconds = 570 }; // 9,5 минут
        var settings = new AppSettings { LoggedIn = true, Tariff = Tariff.Pro50 };
        var result = AccessPolicy.Check(track, false, new LibraryData(), settings);

        Assert.True(result.Allowed);
    }

    [Fact]
    public void Ultra_Allows_Any_Long_Track()
    {
        var track = new TrackItem { DurationSeconds = 7200 };
        var settings = new AppSettings { LoggedIn = true, Tariff = Tariff.Ultra200 };
        var result = AccessPolicy.Check(track, false, new LibraryData(), settings);

        Assert.True(result.Allowed);
    }

    [Fact]
    public void After_5000_Plays_Login_Is_Required()
    {
        var track = new TrackItem { DurationSeconds = 120 };
        var library = new LibraryData { TotalPlayed = 5000 };
        var result = AccessPolicy.Check(track, false, library, new AppSettings());

        Assert.False(result.Allowed);
        Assert.True(result.NeedsLogin);
    }
}
