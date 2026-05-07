using FlexCms.Framework.FeatureFlags;
using Xunit;

namespace FlexCms.Tests.Unit.Phase15;

/// <summary>
/// Locks down the percent-rollout bucketing — the two properties that matter
/// in practice are stability per (user, key) and decorrelation across keys.
/// </summary>
public class FeatureFlagBucketTests
{
    [Fact]
    public void Same_user_and_key_buckets_to_same_value_each_call()
    {
        var u = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var b1 = FcmsFeatureService.StableBucket(u, "ai-suggestions");
        var b2 = FcmsFeatureService.StableBucket(u, "ai-suggestions");
        Assert.Equal(b1, b2);
    }

    [Fact]
    public void Bucket_is_in_range_0_to_99()
    {
        // Spot-check a chunk of users so a bad mod / sign-bug shows up.
        for (var i = 0; i < 200; i++)
        {
            var b = FcmsFeatureService.StableBucket(Guid.NewGuid(), "x");
            Assert.InRange(b, 0, 99);
        }
    }

    [Fact]
    public void Different_keys_for_same_user_are_decorrelated()
    {
        // Same user, different feature keys: across 100 features the
        // distribution of "is X in the cohort" should look roughly uniform —
        // we use a coarser test (just count distinct buckets) so this isn't
        // flaky on the SHA-256 distribution.
        var u = Guid.NewGuid();
        var distinct = new HashSet<int>();
        for (var i = 0; i < 100; i++)
            distinct.Add(FcmsFeatureService.StableBucket(u, $"feature-{i}"));
        // Expect at least 30 distinct buckets out of 100 keys — much
        // tighter than the theoretical max but loose enough that this
        // doesn't fail because of an unlucky seed.
        Assert.True(distinct.Count > 30, $"Buckets only had {distinct.Count} distinct values across 100 keys.");
    }

    [Fact]
    public void Distribution_across_users_is_roughly_uniform()
    {
        // 1000 users, count how many fall in each decile [0,10), [10,20), ..., [90,100).
        // Tolerate up to 4× deviation — generous so this isn't flaky, tight
        // enough to catch a stuck-at-zero or 0/100 bug.
        var counts = new int[10];
        for (var i = 0; i < 1000; i++)
        {
            var b = FcmsFeatureService.StableBucket(Guid.NewGuid(), "rollout-test");
            counts[b / 10]++;
        }
        foreach (var c in counts)
            Assert.InRange(c, 25, 400);   // expected ~100 per decile
    }
}
