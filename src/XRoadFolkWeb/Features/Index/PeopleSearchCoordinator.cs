using Microsoft.Extensions.Caching.Memory;
using XRoadFolkRaw.Lib;
using XRoadFolkWeb.Features.People;

namespace XRoadFolkWeb.Features.Index
{
    public sealed class PeopleSearchCoordinator
    {
        private readonly PeopleService _service;
        private readonly PeopleResponseParser _parser;
        private readonly IMemoryCache _cache;
        private const string ResponseKey = "PeoplePublicInfoResponse";

        public PeopleSearchCoordinator(PeopleService service, PeopleResponseParser parser, IMemoryCache cache)
        {
            _service = service;
            _parser = parser;
            _cache = cache;
        }

        public async Task<(string Xml, string Pretty, List<XRoadFolkWeb.Features.People.PersonRow> Results)> SearchAsync(string? ssn, string? firstName, string? lastName, DateTimeOffset? dateOfBirth, CancellationToken ct = default)
        {
            string xml = await _service.GetPeoplePublicInfoAsync(ssn, firstName, lastName, dateOfBirth, ct).ConfigureAwait(false);
            string pretty = _parser.PrettyFormatXml(xml);
            List<XRoadFolkWeb.Features.People.PersonRow> results = _parser.ParsePeopleList(xml);

            _ = _cache.Set(ResponseKey, results, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
            });

            return (xml, pretty, results);
        }
    }
}
