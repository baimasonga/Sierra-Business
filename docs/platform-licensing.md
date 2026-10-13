# Trust Platform integration for Windows

The MAUI Windows application can now activate a license issued by the existing Trust Platform. Business passwords stay in local Identity; platform credentials are used only for activation and are never included in business databases, backups or the protected license checkpoint. Platform signing private keys never ship in the application.

## Publisher configuration

1. In the platform operator console, register product code `sierra-business` (or use another exact code consistently).
2. Create its plans with the boolean entitlement `business_write: true` for plans permitted to post business activity. Numeric quota values do not enable features. Set trial periods, device limits and offline lease duration on the server.
3. Issue a subscription/trial/perpetual license to the customer's platform organisation through the existing operator workflow. The person activating must have the required platform organisation permissions.
4. Obtain the **public** license verification bundle from your trusted platform administration process. Copy each approved Ed25519 `kid` and public JWK `x` into the configuration below. Do not trust an arbitrary network response as a new trust root. Include approved replacement keys before rotating the server signing key; remove compromised keys through a trusted application/configuration update.
5. Put `licensing.json` in `src/SierraBusiness.Maui` before publishing. The build copies it beside the installed executable. The JSON uses PascalCase property names:

```json
{
  "ApiOrigin": "https://trustplatform-api-production.up.railway.app/",
  "ProductCode": "sierra-business",
  "Keys": [
    { "Kid": "<approved-license-key-id>", "X": "<approved-Ed25519-public-JWK-x>" }
  ],
  "PermissionFeatures": {
    "sales.create": "business_write",
    "inventory.receive": "business_write",
    "accounting.write": "business_write"
  }
}
```

Replace both public-key placeholders before building. `X` is canonical unpadded base64url representing exactly 32 public-key bytes. The API origin must be HTTPS with no path, query, credentials or fragment. No privileged platform API key, database password or signing secret belongs in this file. Keep the installed file protected by the install directory's normal administrator permissions and distribute it with the trusted installer.

Unmapped commercial write permissions require `business_write`. Optional mappings can split write features into server boolean entitlements, for example `payroll_enabled`; valid names use lowercase letters, digits and underscores. Saved-record reads, local user administration/recovery and backups remain available without commercial entitlements; mapping these preservation permissions is rejected rather than silently ignored. Local Identity permissions are still independently required. Alternative-role writes, conditional pricing exceptions, import previews and approval checks consult the same licensed-permission check.

MAUI **Release** builds require licensing by default. Missing or malformed public configuration selects an explicit restricted service: the local workspace opens, records/backups remain available, and commercial writes are denied. Debug builds without configuration retain the existing unlicensed development behavior. An explicit `-p:SierraLicenseRequired=false` creates an unlicensed developer build and must never be used for a paid release. Existing installed binaries are unaffected by these source changes.

## Customer flow

Open the Software license panel, use the platform account email/password (and authenticator or recovery code when enabled), and enter the one-time supplied license key. These credentials differ from the company's local business sign-in. Password, factor and license-key input fields clear after every activation attempt. The platform session token exists only in the activation call's memory.

The panel refreshes once per minute when a verified lease recommends refresh or has expired. Manual refresh is also available. Reads and backups remain available if refresh fails. Transient network failures preserve the last signed lease only until its hard signed expiry; they never extend it. An authoritative forbidden/not-found refresh clears the local grant. A protected-storage write failure restricts commercial access immediately; known revocations and the clock watermark stay in memory and are saved when storage becomes writable again. Existing offline leases cannot learn about a server revocation until reconnecting or expiring.

Deactivation needs an explicit confirmation and server acknowledgement before clearing the local grant. Failed/offline deactivation does not claim a seat was released. Successful deactivation preserves installation identity and business records. Reinstallation does not itself create a new server trial.

## Security and storage

Ed25519 device proofs use the existing canonical newline protocol, JWK thumbprints and one-use server challenges. Each activation/refresh response is verified locally for EdDSA signature, `lease+jws` purpose, pinned key, issuer, product audience, installation, device thumbprint and activation ID. Expiry is a hard boundary; a persisted clock watermark rejects rollback beyond 60 seconds. Cached application actors invoke the current license check on every permission demand, so an earlier sign-in cannot retain expired write access.

One serialized, atomic DPAPI CurrentUser checkpoint stores installation ID, the installation's software Ed25519 private key, activation ID, signed lease and clock watermark. It lives under `%LOCALAPPDATA%\SierraBusiness\Licensing\<company-id>\license.dpapi`, outside the company database/backup directory. Different local companies use separate installation identities and can consume separate server device seats. Do not copy these checkpoints between company installations. Normal installation startup holds the existing per-company process mutex; the client must remain a singleton. Corrupted state is not silently replaced with a fresh identity.

DPAPI protects stored bytes for the Windows account. It is not TPM attestation and cannot stop code already running as that account, an administrator, a patched client, or deliberate checkpoint deletion. Desktop checks are defense in depth; server operations must continue to enforce their own licensing and authorization. Background replication/imported head-office data, database restoration and maintenance jobs are not commercially gated by this desktop actor callback. This integration does not claim all write sources are covered by local anti-tamper enforcement.

## Verification and limits

```powershell
dotnet test SierraBusiness.sln --configuration Release
dotnet workload install maui-windows
dotnet build src/SierraBusiness.Maui/SierraBusiness.Maui.csproj --configuration Release
```

New tests cover valid/tampered/wrong-binding signed leases, a Node.js-signed compatibility vector, hard expiry, restart and clock rollback, Ed25519 challenge proof, activation credentials remaining out of storage, refresh/revocation/deactivation, cached actor enforcement, missing release configuration and safe UI error handling. The native DPAPI test runs on Windows and is explicitly skipped on Linux. The Windows CI workflow builds the actual MAUI host.

A disposable local real-platform test also verified .NET activation, device proof, lease verification, refresh and deactivation. This was not a live Railway/customer-account test. Production still needs your approved product/plans, public signing keys and packaged configuration. Physical Windows installation acceptance, TPM identity, a .NET signed update installer and live billing are separate work; they are not implemented by this integration.
