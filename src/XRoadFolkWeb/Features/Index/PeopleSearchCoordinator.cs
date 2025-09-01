using XRoadFolkRaw.Lib;
using XRoadFolkWeb.Features.People;

namespace XRoadFolkWeb.Features.Index
{
    public sealed class PeopleSearchCoordinator(PeopleService service, PeopleResponseParser parser)
    {
        private readonly PeopleService _service = service;
        private readonly PeopleResponseParser _parser = parser;

        public async Task<(string Xml, string Pretty, List<PersonRow> Results)> SearchAsync(string? ssn, string? firstName, string? lastName, DateTimeOffset? dateOfBirth, CancellationToken ct = default)
        {
            string xml = await _service.GetPeoplePublicInfoAsync(ssn, firstName, lastName, dateOfBirth, ct).ConfigureAwait(false);
            string pretty = _parser.PrettyFormatXml(xml);
            List<PersonRow> results = _parser.ParsePeopleList(xml);
            return (xml, pretty, results);
        }
    }
}
