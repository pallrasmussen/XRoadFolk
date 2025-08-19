using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using XRoad.Config;

namespace XRoadFolkRaw.Lib;

public sealed class PeopleService
{
    private readonly FolkRawClient _client;
    private readonly IConfiguration _config;
    private readonly XRoadSettings _settings;
    private readonly ILogger _log;
    private readonly FolkTokenProviderRaw _tokenProvider;

    public PeopleService(FolkRawClient client, IConfiguration config, XRoadSettings settings, ILogger log)
    {
        _client = client;
        _config = config;
        _settings = settings;
        _log = log;

        _tokenProvider = new FolkTokenProviderRaw(client, ct => client.LoginAsync(
            loginXmlPath: settings.Raw.LoginXmlPath,
            xId: Guid.NewGuid().ToString("N"),
            userId: settings.Auth.UserId,
            username: settings.Auth.Username ?? string.Empty,
            password: settings.Auth.Password ?? string.Empty,
            protocolVersion: settings.Headers.ProtocolVersion,
            clientXRoadInstance: settings.Client.XRoadInstance,
            clientMemberClass: settings.Client.MemberClass,
            clientMemberCode: settings.Client.MemberCode,
            clientSubsystemCode: settings.Client.SubsystemCode,
            serviceXRoadInstance: settings.Service.XRoadInstance,
            serviceMemberClass: settings.Service.MemberClass,
            serviceMemberCode: settings.Service.MemberCode,
            serviceSubsystemCode: settings.Service.SubsystemCode,
            serviceCode: settings.Service.ServiceCode,
            serviceVersion: settings.Service.ServiceVersion ?? "v1",
            ct: ct
        ), refreshSkew: TimeSpan.FromSeconds(60));
    }

    private async Task<string> GetTokenAsync(CancellationToken ct = default)
    {
        string token = await _tokenProvider.GetTokenAsync(ct);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Token provider returned null/empty token.");
        }
        _log.LogInformation("Token acquired (len={Len})", token.Length);
        return token;
    }

    public async Task<string> GetPeoplePublicInfoAsync(string? ssn, string? firstName, string? lastName, DateTimeOffset? dateOfBirth, CancellationToken ct = default)
    {
        string token = await GetTokenAsync(ct);
        string opXml = _config.GetValue<string>("Operations:GetPeoplePublicInfo:XmlPath") ?? "GetPeoplePublicInfo.xml";
        return await _client.GetPeoplePublicInfoAsync(
            xmlPath: opXml,
            xId: Guid.NewGuid().ToString("N"),
            userId: _settings.Auth.UserId,
            token: token,
            protocolVersion: _settings.Headers.ProtocolVersion,
            clientXRoadInstance: _settings.Client.XRoadInstance,
            clientMemberClass: _settings.Client.MemberClass,
            clientMemberCode: _settings.Client.MemberCode,
            clientSubsystemCode: _settings.Client.SubsystemCode,
            serviceXRoadInstance: _settings.Service.XRoadInstance,
            serviceMemberClass: _settings.Service.MemberClass,
            serviceMemberCode: _settings.Service.MemberCode,
            serviceSubsystemCode: _settings.Service.SubsystemCode,
            serviceCode: "GetPeoplePublicInfo",
            serviceVersion: "v1",
            ssn: ssn,
            firstName: firstName,
            lastName: lastName,
            dateOfBirth: dateOfBirth,
            ct: ct);
    }

    public async Task<string> GetPersonAsync(string? publicId, CancellationToken ct = default)
    {
        string token = await GetTokenAsync(ct);
        string personXmlPath = _config.GetValue<string>("Operations:GetPerson:XmlPath") ?? "GetPerson.xml";
        return await _client.GetPersonAsync(
            xmlPath: personXmlPath,
            xId: Guid.NewGuid().ToString("N"),
            userId: _settings.Auth.UserId,
            token: token,
            protocolVersion: _settings.Headers.ProtocolVersion,
            clientXRoadInstance: _settings.Client.XRoadInstance,
            clientMemberClass: _settings.Client.MemberClass,
            clientMemberCode: _settings.Client.MemberCode,
            clientSubsystemCode: _settings.Client.SubsystemCode,
            serviceXRoadInstance: _settings.Service.XRoadInstance,
            serviceMemberClass: _settings.Service.MemberClass,
            serviceMemberCode: _settings.Service.MemberCode,
            serviceSubsystemCode: _settings.Service.SubsystemCode,
            serviceCode: "GetPerson",
            serviceVersion: "v1",
            publicId: publicId,
            includeAddress: _config.GetValue("GetPerson:Include:Address", true),
            includeContact: _config.GetValue("GetPerson:Include:Contact", true),
            includeBirthDate: _config.GetValue("GetPerson:Include:BirthDate", true),
            includeDeathDate: _config.GetValue("GetPerson:Include:DeathDate", false),
            includeGender: _config.GetValue("GetPerson:Include:Gender", true),
            includeMaritalStatus: _config.GetValue("GetPerson:Include:MaritalStatus", false),
            includeCitizenship: _config.GetValue("GetPerson:Include:Citizenship", true),
            includeSsnHistory: _config.GetValue("GetPerson:Include:SsnHistory", false),
            ct: ct);
    }
}
