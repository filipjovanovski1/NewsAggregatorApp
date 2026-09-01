using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NewsApplication.Domain.DTOs.Discovery;
using Xunit;

namespace NewsApplication.Tests.DiscoveryTesting
{
    /*

     Contract tests for the discovery result payload.

     These deserialize Samples/result.example.json — a real 26-source MK/Skopje run — through
     DiscoveryJsonOptions.SnakeCase, the same options the callback controller will use. They
     are deliberately plain unit tests with no WebApplicationFactory, no database and no
     pipeline: the DTO layer is the one piece of this integration that can be verified with
     nothing running.

     What they are actually guarding is drift. The five tables, the upsert SQL and the poller
     are all derived from this shape, so a field that silently stops binding here surfaces
     later as a column full of nulls rather than as an error.

    */
    public sealed class DiscoveryResultDtoTests
    {
        private static readonly string SamplePath =
            Path.Combine(AppContext.BaseDirectory, "Samples", "result.example.json");

        private static DiscoveryResultDTO ReadSample()
        {
            var json = File.ReadAllText(SamplePath);
            var dto = JsonSerializer.Deserialize<DiscoveryResultDTO>(json, DiscoveryJsonOptions.SnakeCase);
            Assert.NotNull(dto);
            return dto!;
        }

        [Fact]
        public void Sample_File_Is_Present_In_Test_Output()
        {
            // Guards the csproj Content/CopyToOutputDirectory item rather than the DTOs: without
            // it every other test here fails with a file-not-found that says nothing useful.
            Assert.True(File.Exists(SamplePath), $"Sample payload not copied to output: {SamplePath}");
        }

        [Fact]
        public void Envelope_Binds()
        {
            var r = ReadSample();

            Assert.Equal(1, r.SchemaVersion);
            Assert.Equal("aca2182e-d409-41df-8972-04d70c632ff9", r.JobId);
            Assert.Equal("completed", r.Status);
            Assert.Null(r.Error);
            Assert.Equal(26, r.Sources.Count);
        }

        [Fact]
        public void Timestamps_Bind_As_Utc_Offsets()
        {
            var r = ReadSample();

            Assert.Equal(
                new DateTimeOffset(2026, 8, 21, 13, 49, 36, TimeSpan.Zero), r.StartedAt);
            Assert.Equal(
                new DateTimeOffset(2026, 8, 21, 13, 50, 56, TimeSpan.Zero), r.FinishedAt);
            Assert.Equal(TimeSpan.Zero, r.StartedAt!.Value.Offset);
        }

        [Fact]
        public void Location_Binds_Including_The_Endonym()
        {
            var r = ReadSample();
            var loc = r.Location;

            Assert.NotNull(loc);
            Assert.Equal("MK", loc!.Iso2);
            Assert.Equal("MKD", loc.Iso3);
            Assert.Equal("North Macedonia", loc.CountryName);
            Assert.Equal("Skopje", loc.City);
            // Non-ASCII round-trip: this is the field the pipeline builds local-language
            // queries from, and mangling it degrades results without failing anything.
            Assert.Equal("Скопје", loc.CityLocalName);
            Assert.Equal("9c5b94b1-35ad-49bb-b118-8e8fc24abf80", loc.CityId);
        }

        [Fact]
        public void Stats_Bind_Including_The_ScreamingSnakeCase_Classified_Keys()
        {
            var r = ReadSample();
            var s = r.Stats;

            Assert.NotNull(s);
            Assert.Equal(38, s!.Discovered);
            Assert.Equal(5, s.CrawlFailures);
            Assert.Equal(0, s.ClassifyErrors);
            Assert.Equal(26, s.Returned);

            Assert.NotNull(s.Classified);
            Assert.Equal(15, s.Classified!.NewsSource);
            Assert.Equal(11, s.Classified.DiscoverySource);
            Assert.Equal(12, s.Classified.Reject);
        }

        [Fact]
        public void Absent_Query_Stats_Bind_As_Null_Not_Zero()
        {
            // The single most load-bearing assertion in this file. This run reused the discovery
            // cache and issued no searches, so the pipeline omitted both fields entirely. Making
            // them required — or defaulting them to 0 — throws, or silently reports "0 queries
            // run" for every cached run thereafter.
            var r = ReadSample();

            Assert.Null(r.Stats!.QueriesRun);
            Assert.Null(r.Stats.QueriesEmpty);
        }

        [Fact]
        public void Warnings_Are_Advisory_And_Coexist_With_A_Completed_Run()
        {
            var r = ReadSample();

            Assert.Equal("completed", r.Status);
            Assert.Single(r.Warnings);
            Assert.Contains("reused cache", r.Warnings[0]);
        }

        [Fact]
        public void First_Source_Binds_Field_For_Field()
        {
            var r = ReadSample();
            var src = r.Sources[0];

            Assert.Equal("novamakedonija.com.mk", src.Domain);
            Assert.Equal("Нова Македонија", src.Name);
            Assert.Equal("https://novamakedonija.com.mk/", src.Url);
            Assert.True(src.SourceFactsRefreshed);
            Assert.Equal("mk", src.Language);
            Assert.Equal("NEWS_SOURCE", src.Classification);
            Assert.Equal(0.98, src.Confidence);
            Assert.Equal(46, src.Categories.Count);
            Assert.Equal("ekonomija", src.Categories[0]);

            var feed = Assert.Single(src.Feeds);
            Assert.Equal("https://novamakedonija.com.mk/feed/", feed.Url);
            Assert.Equal("Нова Македонија", feed.Title);
            Assert.Equal(10, feed.EntryCount);
            Assert.Equal(new DateTimeOffset(2026, 8, 21, 12, 25, 10, TimeSpan.Zero), feed.LatestEntry);
            Assert.True(feed.HasFullContent);
            Assert.Equal("mk", feed.Language);
            Assert.Equal(0.0, feed.ExternalLinkRatio);
            Assert.Equal(1, feed.DistinctSources);

            Assert.NotNull(src.Relevance);
            Assert.Equal(100.0, src.Relevance!.Score);
            Assert.Equal("high", src.Relevance.PollingTier);
            Assert.Equal(4, src.Relevance.SearchOccurrences);
            Assert.Equal(3, src.Relevance.MatchedQueries.Count);

            Assert.NotNull(src.Evidence);
            Assert.Equal(293, src.Evidence!.ArticleLikePaths);
            Assert.True(src.Evidence.HasDatePatterns);
            Assert.Equal(14, src.Evidence.AuthorCount);
            Assert.False(string.IsNullOrWhiteSpace(src.Evidence.Reason));
        }

        [Fact]
        public void Every_Source_Has_The_Fields_The_Upsert_Keys_On()
        {
            // Domain keys NewsSource; Classification and Relevance decide what the poller does
            // with it. Anything missing here breaks an upsert rather than a read.
            var r = ReadSample();

            Assert.All(r.Sources, s =>
            {
                Assert.False(string.IsNullOrWhiteSpace(s.Domain));
                Assert.Contains(s.Classification, new[] { "NEWS_SOURCE", "DISCOVERY_SOURCE" });
                Assert.NotNull(s.Relevance);
                Assert.Contains(s.Relevance!.PollingTier, new[] { "high", "medium", "low", "backup" });
                Assert.NotNull(s.Confidence);
                Assert.InRange(s.Confidence!.Value, 0.0, 1.0);
                Assert.NotNull(s.Relevance.Score);
                Assert.InRange(s.Relevance.Score!.Value, 0.0, 100.0);
            });
        }

        [Fact]
        public void No_Rejects_Are_Ever_Sent()
        {
            var r = ReadSample();

            Assert.DoesNotContain(r.Sources, s => s.Classification == "REJECT");
            // 15 NEWS_SOURCE + 11 DISCOVERY_SOURCE == 26 returned; the 12 rejects appear only
            // as a count.
            Assert.Equal(r.Stats!.Returned, r.Sources.Count);
        }

        [Fact]
        public void Sources_With_No_Feeds_Bind_As_Empty_Not_Null()
        {
            var r = ReadSample();
            var feedless = r.Sources.Where(s => s.Feeds.Count == 0).ToList();

            // 14 of the 26 — mostly aggregators and directories. A source is worth storing
            // without a feed; it just gets nothing to poll.
            Assert.NotEmpty(feedless);
            Assert.Contains(feedless, s => s.Domain == "time.mk");
            Assert.All(r.Sources, s => Assert.NotNull(s.Feeds));
        }

        [Fact]
        public void Nullable_Feed_Fields_Really_Do_Arrive_Null()
        {
            // Not hypothetical: two feeds in this one sample omit them. These map to nullable
            // columns, and a NOT NULL constraint on either would fail on the very first import.
            var r = ReadSample();
            var feeds = r.Sources.SelectMany(s => s.Feeds).ToList();

            Assert.Contains(feeds, f => f.Title is null);
            Assert.Contains(feeds, f => f.Language is null);
        }

        [Fact]
        public void Categories_Are_Unbounded_And_Messy()
        {
            // Sets the floor for the column: no length assumption, no enum, no lookup table.
            var r = ReadSample();
            var worst = r.Sources.OrderByDescending(s => s.Categories.Count).First();

            Assert.Equal("apnews.com", worst.Domain);
            Assert.Equal(73, worst.Categories.Count);
        }

        [Fact]
        public void Failed_Payload_Binds_With_No_Sources_And_A_Stage()
        {
            // The sample is a success case, so the failure branch is exercised against the
            // contract instead. Both shutdown-drain stages ("queued", "cancelled") arrive in
            // this shape, which is what lets the callback use a single deserializer.
            const string json = """
                {
                  "schema_version": 1,
                  "job_id": "aca2182e-d409-41df-8972-04d70c632ff9",
                  "status": "failed",
                  "location": { "iso2": "MK", "iso3": null, "country_name": null,
                                "city": null, "city_local_name": null, "city_id": null },
                  "started_at": "2026-08-21T13:49:36Z",
                  "finished_at": "2026-08-21T13:49:40Z",
                  "stats": null,
                  "warnings": [],
                  "error": { "stage": "queued", "type": "ShutdownError",
                             "message": "pipeline received SIGTERM" },
                  "sources": []
                }
                """;

            var r = JsonSerializer.Deserialize<DiscoveryResultDTO>(json, DiscoveryJsonOptions.SnakeCase);

            Assert.NotNull(r);
            Assert.Equal("failed", r!.Status);
            Assert.Empty(r.Sources);
            Assert.Null(r.Stats);
            Assert.NotNull(r.Error);
            Assert.Equal("queued", r.Error!.Stage);
            Assert.Equal("ShutdownError", r.Error.Type);

            // A country-level target has no city, and the nulls must survive as nulls.
            Assert.Equal("MK", r.Location!.Iso2);
            Assert.Null(r.Location.City);
            Assert.Null(r.Location.CityId);
        }

        [Fact]
        public void Zero_Source_Completed_Is_Not_A_Failure()
        {
            // Feeds ConsecutiveEmptyRuns, never ConsecutiveFailures. Asserted at the DTO level
            // so the distinction is already visible when the import service is written.
            const string json = """
                {
                  "schema_version": 1,
                  "job_id": "aca2182e-d409-41df-8972-04d70c632ff9",
                  "status": "completed",
                  "location": { "iso2": "TD" },
                  "started_at": "2026-08-21T13:49:36Z",
                  "finished_at": "2026-08-21T14:02:11Z",
                  "stats": { "discovered": 0, "crawl_failures": 0, "classify_errors": 0,
                             "classified": { "NEWS_SOURCE": 0, "DISCOVERY_SOURCE": 0, "REJECT": 0 },
                             "returned": 0 },
                  "warnings": [],
                  "error": null,
                  "sources": []
                }
                """;

            var r = JsonSerializer.Deserialize<DiscoveryResultDTO>(json, DiscoveryJsonOptions.SnakeCase);

            Assert.NotNull(r);
            Assert.Equal("completed", r!.Status);
            Assert.Null(r.Error);
            Assert.Empty(r.Sources);
            Assert.Equal(0, r.Stats!.Discovered);
        }
    }
}