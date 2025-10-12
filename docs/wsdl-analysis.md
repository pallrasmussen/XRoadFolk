# WSDL Analysis: X-Road Folk Service

## Overview
This document provides a comprehensive analysis of the X-Road Folk web service based on the SOAP XML templates used in this application. The service implements the X-Road protocol version 4.0 for secure data exchange.

## Service Endpoint
- **Protocol**: SOAP 1.1 over HTTP/HTTPS
- **Namespace**: `http://us-folk-v2.x-road.eu/producer`
- **X-Road Protocol Version**: 4.0
- **SOAP Envelope Namespace**: `http://schemas.xmlsoap.org/soap/envelope/`

## X-Road Identifiers
The service uses X-Road standard identifier namespaces:
- **Identifiers**: `http://x-road.eu/xsd/identifiers`
- **X-Road Schema**: `http://x-road.eu/xsd/xroad.xsd`

## Operations

### 1. Login
**Purpose**: Authenticates a user and returns an authentication token for subsequent operations.

**Request Structure**:
```xml
<prod:Login>
  <request>
    <username>string</username>
    <password>string</password>
  </request>
</prod:Login>
```

**Parameters**:
- `username` (string, required): User's login identifier
- `password` (string, required): User's password

**Response**: Returns an authentication token used in subsequent operations.

**Security**: Credentials are transmitted in the SOAP body. HTTPS should be used for secure transmission.

---

### 2. GetPeoplePublicInfo
**Purpose**: Searches for people based on various criteria and returns public information.

**Service Details**:
- **Service Code**: `GetPeoplePublicInfo`
- **Service Version**: `v1`

**Request Structure**:
```xml
<prod:GetPeoplePublicInfo>
  <request>
    <requestBody>
      <ListOfPersonPublicInfoCriteria>
        <PersonPublicInfoCriteria>
          <SSN>string (optional)</SSN>
          <FirstName>string (optional)</FirstName>
          <LastName>string (optional)</LastName>
          <DateOfBirth>dateTime (optional)</DateOfBirth>
        </PersonPublicInfoCriteria>
      </ListOfPersonPublicInfoCriteria>
    </requestBody>
    <requestHeader>
      <token>string (required)</token>
    </requestHeader>
  </request>
</prod:GetPeoplePublicInfo>
```

**Search Criteria** (all optional, at least one should be provided):
- `SSN` (string): Social Security Number / National Identification Number
- `FirstName` (string): Person's first name (partial match supported)
- `LastName` (string): Person's last name (partial match supported)
- `DateOfBirth` (dateTime): Date of birth in ISO 8601 format

**Authentication**:
- `token` (string, required): Authentication token obtained from Login operation

**Response Structure**:
```xml
<GetPeoplePublicInfoResponse>
  <ListOfPersonPublicInfo>
    <PersonPublicInfo>
      <PublicId>string</PublicId>
      <PersonId>string</PersonId>
      <Names>
        <Name>
          <Type>FirstName|LastName</Type>
          <Order>integer</Order>
          <Value>string</Value>
        </Name>
      </Names>
      <SSN>string</SSN>
      <CivilStatusDate>dateTime</CivilStatusDate>
    </PersonPublicInfo>
  </ListOfPersonPublicInfo>
  <ListOfPersonPublicInfoCriteria>
    <!-- Echo of search criteria -->
  </ListOfPersonPublicInfoCriteria>
</GetPeoplePublicInfoResponse>
```

**Response Elements**:
- `PublicId`: Public identifier for the person
- `PersonId`: Internal person identifier
- `Names`: Collection of name elements (can include multiple first names)
  - `Type`: Name type (FirstName, LastName)
  - `Order`: Ordering for multiple names of the same type
  - `Value`: The actual name value
- `SSN`: Social Security Number
- `CivilStatusDate`: Date related to civil status

---

### 3. GetPerson
**Purpose**: Retrieves detailed information for a specific person using various identifier types with selective data inclusion.

**Service Details**:
- **Service Code**: `GetPerson`
- **Service Version**: `v1`

**Request Structure**:
```xml
<prod:GetPerson>
  <request>
    <requestBody>
      <!-- One of the following identifiers is required -->
      <Id>string (internal ID)</Id>
      <!-- OR -->
      <PublicId>string (public identifier)</PublicId>
      <!-- OR -->
      <SSN>string (social security number)</SSN>
      <!-- OR -->
      <ExternalId>string (external identifier)</ExternalId>
      
      <!-- Optional include flags for selective data retrieval -->
      <Include>
        <Addresses>bool</Addresses>
        <AddressesHistory>bool</AddressesHistory>
        <BiologicalParents>bool</BiologicalParents>
        <ChurchMembership>bool</ChurchMembership>
        <ChurchMembershipHistory>bool</ChurchMembershipHistory>
        <Citizenships>bool</Citizenships>
        <CitizenshipsHistory>bool</CitizenshipsHistory>
        <CivilStatus>bool</CivilStatus>
        <CivilStatusHistory>bool</CivilStatusHistory>
        <ForeignSsns>bool</ForeignSsns>
        <Incapacity>bool</Incapacity>
        <IncapacityHistory>bool</IncapacityHistory>
        <JuridicalChildren>bool</JuridicalChildren>
        <JuridicalChildrenHistory>bool</JuridicalChildrenHistory>
        <JuridicalParents>bool</JuridicalParents>
        <JuridicalParentsHistory>bool</JuridicalParentsHistory>
        <Names>bool</Names>
        <NamesHistory>bool</NamesHistory>
        <Notes>bool</Notes>
        <NotesHistory>bool</NotesHistory>
        <Postbox>bool</Postbox>
        <SpecialMarks>bool</SpecialMarks>
        <SpecialMarksHistory>bool</SpecialMarksHistory>
        <Spouse>bool</Spouse>
        <SpouseHistory>bool</SpouseHistory>
        <Ssn>bool</Ssn>
        <SsnHistory>bool</SsnHistory>
      </Include>
    </requestBody>
    <requestHeader>
      <token>string (required)</token>
    </requestHeader>
  </request>
</prod:GetPerson>
```

**Identifiers** (exactly one required):
- `Id` (string): Internal person identifier
- `PublicId` (string): Public person identifier (most commonly used)
- `SSN` (string): Social Security Number / National ID
- `ExternalId` (string): External system identifier

**Authentication**:
- `token` (string, required): Authentication token from Login operation

**Optional Include Flags**:
Each boolean flag controls whether that section is included in the response:
- `Addresses`: Current address information
- `AddressesHistory`: Historical addresses
- `BiologicalParents`: Biological parent information
- `ChurchMembership`: Current church membership
- `ChurchMembershipHistory`: Historical church memberships
- `Citizenships`: Current citizenship(s)
- `CitizenshipsHistory`: Historical citizenship records
- `CivilStatus`: Current civil/marital status
- `CivilStatusHistory`: Historical civil status changes
- `ForeignSsns`: Foreign social security numbers
- `Incapacity`: Legal incapacity status
- `IncapacityHistory`: Historical incapacity records
- `JuridicalChildren`: Legal children
- `JuridicalChildrenHistory`: Historical child relationships
- `JuridicalParents`: Legal parents/guardians
- `JuridicalParentsHistory`: Historical parent/guardian records
- `Names`: Current names
- `NamesHistory`: Name change history
- `Notes`: Administrative notes
- `NotesHistory`: Historical notes
- `Postbox`: Postal box information
- `SpecialMarks`: Special registration marks
- `SpecialMarksHistory`: Historical special marks
- `Spouse`: Current spouse information
- `SpouseHistory`: Previous spouses
- `Ssn`: Social security number(s)
- `SsnHistory`: SSN change history

**Configuration**:
Include flags can be configured in `appsettings.json`:
```json
{
  "Operations": {
    "GetPerson": {
      "Request": {
        "Include": {
          "Names": true,
          "Addresses": true,
          "Citizenships": true
        }
      }
    }
  }
}
```

**Response**: Returns comprehensive person details based on the include flags specified. Basic person information is always returned, with optional sections included based on flags.

---

## X-Road SOAP Header

All operations include a standard X-Road header structure:

```xml
<soapenv:Header>
  <xrd:protocolVersion>4.0</xrd:protocolVersion>
  <xrd:id>unique-message-id</xrd:id>
  <xrd:client id:objectType="SUBSYSTEM">
    <id:xRoadInstance>instance-code</id:xRoadInstance>
    <id:memberClass>member-class</id:memberClass>
    <id:memberCode>member-code</id:memberCode>
    <id:subsystemCode>subsystem-code</id:subsystemCode>
  </xrd:client>
  <xrd:service id:objectType="SERVICE">
    <id:xRoadInstance>instance-code</id:xRoadInstance>
    <id:memberClass>member-class</id:memberClass>
    <id:memberCode>member-code</id:memberCode>
    <id:subsystemCode>subsystem-code</id:subsystemCode>
    <id:serviceCode>operation-name</id:serviceCode>
    <id:serviceVersion>v1</id:serviceVersion>
  </xrd:service>
</soapenv:Header>
```

### Header Elements:
- **protocolVersion**: X-Road protocol version (4.0)
- **id**: Unique identifier for the message (for tracking and correlation)
- **client**: Identifies the client subsystem
  - `xRoadInstance`: X-Road instance identifier
  - `memberClass`: Member class (e.g., GOV, COM)
  - `memberCode`: Organization code
  - `subsystemCode`: Subsystem identifier
- **service**: Identifies the target service
  - Similar structure to client
  - `serviceCode`: The operation name (Login, GetPeoplePublicInfo, GetPerson)
  - `serviceVersion`: Service version (v1)

## Data Types

### Common Types:
- **String**: Text data (UTF-8 encoded)
- **DateTime**: ISO 8601 format (e.g., `1990-01-31T00:00:00Z`)
- **Integer**: Numeric values for ordering and identifiers

### Complex Types:

#### PersonPublicInfo
Represents public information about a person including identifiers, names, and demographic data.

#### Name
Represents a name component with type classification and ordering support for multiple values.

## Authentication Flow

1. **Initial Authentication**:
   ```
   Client -> Login(username, password) -> Server
   Server -> token -> Client
   ```

2. **Subsequent Operations**:
   ```
   Client -> Operation(token, parameters) -> Server
   Server -> Response -> Client
   ```

3. **Token Management**:
   - Token obtained from Login operation
   - Token passed in `requestHeader` section of subsequent requests
   - Token should be cached and reused for multiple operations
   - Token expiration handling should be implemented (re-authenticate on failure)

## Security Considerations

### Transport Security:
- **HTTPS Required**: All operations should use HTTPS to protect credentials and sensitive data
- **Client Certificate**: X.509 client certificate authentication supported via X-Road infrastructure
- **Server Certificate Validation**: Configurable via `Http:BypassServerCertificateValidation` (should be false in production)

### Data Protection:
- **Token Masking**: Authentication tokens are masked in logs (controlled by `Logging:MaskTokens`)
- **PII Sanitization**: Personal identifiable information is sanitized in logs outside Development environment
- **SOAP Payload Sanitization**: Using `SafeSoapLogger` and `SoapSanitizer` utilities

### X-Road Security:
- **Message Signing**: X-Road infrastructure handles message signing
- **Encryption**: End-to-end encryption at X-Road security server level
- **Access Control**: X-Road security servers enforce access policies

## Configuration

### Required Configuration Keys:
```json
{
  "XRoad": {
    "BaseUrl": "https://xroad-security-server/",
    "Client": {
      "XRoadInstance": "instance",
      "MemberClass": "class",
      "MemberCode": "code",
      "SubsystemCode": "subsystem"
    },
    "Service": {
      "XRoadInstance": "instance",
      "MemberClass": "class",
      "MemberCode": "service-code",
      "SubsystemCode": "folk-subsystem"
    },
    "Certificate": {
      "PfxPath": "/path/to/cert.pfx",
      "PfxPassword": "password",
      "WarnIfExpiresInDays": 30
    },
    "TokenInsert": {
      "Enabled": true,
      "XPath": "//requestHeader/token"
    }
  },
  "Operations": {
    "GetPeoplePublicInfo": {
      "XmlPath": "optional-custom-template-path"
    },
    "GetPerson": {
      "XmlPath": "optional-custom-template-path"
    }
  }
}
```

## Error Handling

### HTTP Errors:
- **401 Unauthorized**: Invalid credentials or expired token
- **403 Forbidden**: Access denied to requested service
- **500 Internal Server Error**: Service-side processing error
- **503 Service Unavailable**: X-Road infrastructure unavailable

### SOAP Faults:
Standard SOAP fault structure with X-Road specific error codes and messages.

### Retry Strategy:
Configurable retry policy via `Retry:Http` settings:
- `Attempts`: Number of retry attempts
- `BaseDelayMs`: Base delay between retries
- `JitterMs`: Random jitter to prevent thundering herd

## Response Processing

### XML Parsing:
- **Safe XML Reader**: Uses `DepthLimitingXmlReader` to prevent DoS attacks
- **DTD Processing**: Prohibited for security
- **Entity Expansion**: Disabled (MaxCharactersFromEntities = 0)
- **Document Size Limit**: 10 MB maximum

### Response Flattening:
The `PeopleResponseParser` provides methods to:
- Parse structured person data: `ParsePeopleList(xml)`
- Flatten XML to key-value pairs: `FlattenResponse(xml)`
- Pretty-format XML: `PrettyFormatXml(xml)`

### Namespace Handling:
- Parser ignores XML namespaces
- Element matching by local name only
- Supports mixed namespace prefixes

## Performance Considerations

### Template Caching:
- XML templates are cached in memory after first load
- Cache key: template path (case-insensitive)
- Missing templates cached with 30-second TTL

### Token Caching:
- Authentication tokens cached per X-Road configuration
- Cache key: hash of X-Road settings
- Reduces authentication overhead

### HTTP Client:
- Prefer `IHttpClientFactory` for connection pooling
- Configurable timeout (default from HTTP client configuration)
- Keep-alive connections for multiple requests

## Metrics and Observability

### OpenTelemetry Metrics:
- SOAP operation counters
- Request duration histograms
- Retry attempt counters
- Token acquisition timing

### Logging:
Three log kinds:
- **soap**: SOAP-specific operations (request/response)
- **http**: HTTP client operations
- **app**: Application-level events

### Tracing:
- OpenTelemetry distributed tracing support
- Spans for each SOAP operation
- X-Road message ID correlation

## Testing

### Sample SOAP Responses:
Test files contain examples:
- `PeopleResponseParserTests.cs`: Unit tests for parser
- `PeopleResponseParserE2eTests.cs`: End-to-end parsing tests

### Test Data Structure:
```xml
<PersonPublicInfo>
  <PublicId>AAA111</PublicId>
  <Names>
    <Name><Type>FirstName</Type><Order>1</Order><Value>Mary</Value></Name>
    <Name><Type>FirstName</Type><Order>2</Order><Value>Jane</Value></Name>
    <Name><Type>LastName</Type><Value>Doe</Value></Name>
  </Names>
  <SSN>010203123</SSN>
  <CivilStatusDate>1990-01-31T00:00:00Z</CivilStatusDate>
</PersonPublicInfo>
```

## References

### Internal Documentation:
- [Configuration Guide](configuration.md) - Detailed configuration options
- [Logging Architecture](logging.md) - Logging internals and tuning

### X-Road Resources:
- X-Road Protocol: https://x-road.eu/
- X-Road Message Protocol v4.0 Specification
- X-Road Security Server Setup Guide

### Implementation Files:
- SOAP Templates: `src/XRoadFolkRaw.Lib/Resources/*.xml`
- Client: `src/XRoadFolkRaw.Lib/FolkRawClient.cs`
- Service Layer: `src/XRoadFolkRaw.Lib/PeopleService.cs`
- Response Parser: `src/XRoadFolkWeb/Features/People/PeopleResponseParser.cs`

## Revision History

- **2025-10-12**: Initial WSDL analysis document created based on XML SOAP templates
