using System.Reflection;
using System.Text.Json;
using Xunit;

namespace AuthKit.Core.Tests;

/// <summary>
/// Guards contracts/authkit.contract.json. The TypeScript client runs an equivalent test, so a
/// contract change that is not applied on both sides fails the build.
/// </summary>
public sealed class ContractParityTests
{
    private static readonly Dictionary<string, Type> ClrTypes = new(StringComparer.Ordinal)
    {
        ["AuthUser"] = typeof(AuthUser),
        ["AuthSession"] = typeof(AuthSession),
        ["AuthErrorCode"] = typeof(AuthErrorCode),
        ["AuthError"] = typeof(AuthError),
        ["AuthNextStep"] = typeof(AuthNextStep),
        ["AuthResult"] = typeof(AuthResult),
        ["AuthState"] = typeof(AuthState),
        ["AuthCapabilities"] = typeof(AuthCapabilities),
        ["AuthTheme"] = typeof(AuthTheme),
        ["AuthMessageKey"] = typeof(AuthMessageKey),
        ["AuthIdentity"] = typeof(AuthIdentity),
        ["AuthDocumentType"] = typeof(AuthDocumentType),
        ["AuthAssuranceLevel"] = typeof(AuthAssuranceLevel),
        ["AuthFederationMethod"] = typeof(AuthFederationMethod),
        ["AuthFederationProvider"] = typeof(AuthFederationProvider),
        ["AuthProviderSnapshot"] = typeof(AuthProviderSnapshot),
        ["AuthProviderRef"] = typeof(AuthProviderRef)
    };

    private static readonly Dictionary<string, Type> FieldTypes = new(StringComparer.Ordinal)
    {
        ["string"] = typeof(string),
        ["string?"] = typeof(string),
        ["bool"] = typeof(bool),
        ["int?"] = typeof(int?),
        ["datetime"] = typeof(DateTimeOffset),
        ["datetime?"] = typeof(DateTimeOffset?),
        ["date?"] = typeof(DateOnly?),
        ["record<string,string>"] = typeof(IReadOnlyDictionary<string, string>),
        ["string[]"] = typeof(IReadOnlyList<string>),
        ["AuthUser"] = typeof(AuthUser),
        ["AuthSession?"] = typeof(AuthSession),
        ["AuthError?"] = typeof(AuthError),
        ["AuthNextStep"] = typeof(AuthNextStep),
        ["AuthErrorCode"] = typeof(AuthErrorCode),
        ["AuthIdentity?"] = typeof(AuthIdentity),
        ["AuthProviderSnapshot?"] = typeof(AuthProviderSnapshot),
        ["AuthFederationProvider[]"] = typeof(IReadOnlyList<AuthFederationProvider>),
        ["AuthFederationMethod[]"] = typeof(IReadOnlyList<AuthFederationMethod>),
        ["AuthAssuranceLevel[]"] = typeof(IReadOnlyList<AuthAssuranceLevel>),
        ["AuthAssuranceLevel"] = typeof(AuthAssuranceLevel),
        ["AuthDocumentType"] = typeof(AuthDocumentType)
    };

    private static JsonDocument LoadContract()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "contracts", "authkit.contract.json");
        Assert.True(File.Exists(path), $"Contract file not found at {path}.");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    [Fact]
    public void Every_contract_type_exists_in_the_dotnet_sdk()
    {
        using var contract = LoadContract();
        var declared = contract.RootElement.GetProperty("types").EnumerateObject().Select(p => p.Name).ToArray();

        Assert.Equal(declared.OrderBy(n => n, StringComparer.Ordinal), ClrTypes.Keys.OrderBy(n => n, StringComparer.Ordinal));
    }

    [Fact]
    public void Every_contract_field_matches_a_public_property()
    {
        using var contract = LoadContract();

        foreach (var type in contract.RootElement.GetProperty("types").EnumerateObject())
        {
            if (!type.Value.TryGetProperty("fields", out var fields))
            {
                continue;
            }

            var clr = ClrTypes[type.Name];

            foreach (var field in fields.EnumerateObject())
            {
                var property = clr.GetProperty(
                    field.Name,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

                Assert.NotNull(property);
                Assert.True(
                    FieldTypes.TryGetValue(field.Value.GetString()!, out var expected),
                    $"Unknown contract type '{field.Value}' for {type.Name}.{field.Name}.");
                Assert.Equal(expected, property!.PropertyType);
            }
        }
    }

    [Fact]
    public void Enum_values_match_the_contract()
    {
        using var contract = LoadContract();
        var types = contract.RootElement.GetProperty("types");

        Assert.Equal(
            types.GetProperty("AuthErrorCode").GetProperty("enum").EnumerateArray().Select(v => v.GetString()),
            AuthContractValues.ErrorCodes.Values);

        Assert.Equal(
            types.GetProperty("AuthState").GetProperty("enum").EnumerateArray().Select(v => v.GetString()),
            AuthContractValues.States.Values);

        Assert.Equal(
            types.GetProperty("AuthNextStep").GetProperty("enum").EnumerateArray().Select(v => v.GetString()),
            AuthContractValues.NextSteps.Values);

        Assert.Equal(
            types.GetProperty("AuthMessageKey").GetProperty("enum").EnumerateArray().Select(v => v.GetString()),
            AuthContractValues.MessageKeys.Values);
    }

    [Fact]
    public void Every_contract_operation_exists_on_a_dotnet_contract_interface()
    {
        using var contract = LoadContract();
        var methods = new[] { typeof(IAuthProvider), typeof(IServerDrivenAuthProvider) }
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            .Select(m => m.Name)
            .ToArray();

        foreach (var operation in contract.RootElement.GetProperty("operations").EnumerateArray())
        {
            var name = operation.GetProperty("name").GetString()!;
            var expected = char.ToUpperInvariant(name[0]) + name[1..] + "Async";

            Assert.Contains(expected, methods);
        }
    }
}
