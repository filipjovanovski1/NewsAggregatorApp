using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using System.Linq; // for .Select
using NewsApplication.Service.Interfaces;
using NewsApplication.Service.Implementations;

namespace NewsApplication.Tests.QueryTokenizerTesting
{
    

    public sealed class QueryTokenizerTests
    {
        private readonly IQueryTokenizer _t = new QueryTokenizer();

        [Fact]
        public void Split_RawTokens_TypicalExample()
        {
            var raw = "San José,  CR  sports";
            var tokens = _t.Split(raw);
            Assert.Equal(new[] { "San", "José", "CR", "sports" }, tokens);
        }

        [Fact]
        public void Normalize_MatchesAcceptance()
        {
            var raw = "San José,  CR  sports";
            var tokens = _t.Split(raw);
            var normalized = tokens.Select(_t.Normalize).ToArray();
            Assert.Equal(new[] { "san", "jose", "cr", "sports" }, normalized);
        }

        [Theory]
        [InlineData("  a  b   c ", new[] { "a", "b", "c" })]
        [InlineData("a,b.c", new[] { "a", "b", "c" })]
        [InlineData("a,,b...c   d", new[] { "a", "b", "c", "d" })]
        public void Split_Separators_CommaDotWhitespace(string input, string[] expected)
        {
            var tokens = _t.Split(input);
            Assert.Equal(expected, tokens);
        }

        [Theory]
        [InlineData("São", "sao")]
        [InlineData("Łódź", "lodz")]
        [InlineData("Göteborg", "goteborg")]
        [InlineData("München", "munchen")]
        [InlineData("İzmir", "izmir")]
        public void Normalize_Diacritics_Removed(string input, string expected)
        {
            Assert.Equal(expected, _t.Normalize(input));
        }

        [Theory]
        [InlineData("straße", "strasse")]
        [InlineData("Ærø", "aero")]
        [InlineData("Þór", "thor")]
        public void Normalize_ExtraMaps_Handled(string input, string expected)
        {
            Assert.Equal(expected, _t.Normalize(input));
        }

        [Fact]
        public void Normalize_EmptyAndNull_Safe()
        {
            Assert.Equal("", _t.Normalize(""));
            Assert.Equal("", _t.Normalize(null!));
        }

        [Fact]
        public void Split_EmptyAndWhitespace_Safe()
        {
            Assert.Empty(_t.Split(""));
            Assert.Empty(_t.Split("   , ,  .  "));
        }
    }

}
