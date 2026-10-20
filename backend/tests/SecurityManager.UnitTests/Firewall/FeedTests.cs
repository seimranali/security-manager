using SecurityManager.Firewall.Domain.Enums;
using SecurityManager.Firewall.Domain.Services;

namespace SecurityManager.UnitTests.Firewall;

public class FeedTests
{
    [Fact]
    public void Parses_spamhaus_drop_format_with_comments()
    {
        var parsed = FeedParser.Parse("; Spamhaus DROP List\n1.10.16.0/20 ; SBL256894\n2.56.192.0/22 ; SBL459831\n", IpListType.Block);
        Assert.Equal(["1.10.16.0/20", "2.56.192.0/22"], parsed.Addresses);
    }

    [Fact]
    public void Never_blocks_private_or_reserved_ranges_from_a_feed()
    {
        // FireHOL level1 includes bogons; blocking them would cut the host off from its LAN.
        var parsed = FeedParser.Parse("# firehol\n10.0.0.0/8\n192.168.0.0/16\n127.0.0.1\n5.188.10.0/23\n", IpListType.Block);
        Assert.Equal(["5.188.10.0/23"], parsed.Addresses);
        Assert.Equal(3, parsed.Skipped);
    }

    [Fact]
    public void Skips_invalid_duplicate_and_too_broad_entries()
    {
        var parsed = FeedParser.Parse("203.0.113.5\n203.0.113.5\nnot-an-ip\n1.0.0.0/4\n198.51.100.7 score=9\n", IpListType.Block);
        Assert.Equal(["203.0.113.5", "198.51.100.7"], parsed.Addresses);
        Assert.Equal(2, parsed.Skipped);
    }
}
