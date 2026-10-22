# Cl@ve protocol fixtures

Anonymised protocol messages used by `tests/dotnet/AuthKit.Clave.Tests`.

- `response-template.xml` is the assertion body. Every `${PLACEHOLDER}` is replaced by the test
  harness, and the assertion is signed in-process with a throwaway certificate created for the test
  run. No private key, real entity id, certificate or citizen document is stored in the repository.

The tests derive these cases from the same template:

| Case                 | Mutation                                  | Expected reason       |
| -------------------- | ----------------------------------------- | --------------------- |
| valid                | none                                      | accepted              |
| unsigned             | no signature                              | `SignatureInvalid`    |
| foreign key          | signed with an attacker certificate       | `SignatureInvalid`    |
| tampered             | attribute edited after signing            | `SignatureInvalid`    |
| DTD                  | `<!DOCTYPE>` prepended                    | `UnsafeXml`           |
| wrong audience       | `Audience` changed                        | `AudienceMismatch`    |
| wrong destination    | `Destination` changed                     | `DestinationMismatch` |
| wrong issuer         | `Issuer` changed                          | `IssuerMismatch`      |
| replayed assertion   | same assertion id, second pending request | `ReplayDetected`      |
| expired assertion    | `NotOnOrAfter` in the past                | `AssertionExpired`    |
| level downgrade      | `acr` substantial, request high           | `LevelBelowRequested` |
| malformed document   | bad DNI/NIE check letter                  | `DocumentInvalid`     |
| missing document     | no `PersonIdentifier`                     | `SubjectMissing`      |
| unknown request      | `InResponseTo` never issued               | `RequestUnknown`      |
| expired request      | pending request lifetime elapsed          | `RequestExpired`      |
| relay state mismatch | `RelayState` changed                      | `RelayStateMismatch`  |
| gateway error        | second-level status code                  | `StatusNotSuccess`    |

Everything runs offline. There is no call to the real gateway anywhere in the suite.
