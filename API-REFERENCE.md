# eero Desktop (Unofficial) — API Reference

Version 1.1.0 · November 2026 · by Carlos B. (Los33)

This reference covers every web API request eero Desktop makes. For each endpoint it gives the method, the path, the encoding, every field the app sends with the values and limits it accepts, the response fields it reads, and the conditions that decide whether the request is allowed. It also covers the outside services the app talks to: Amazon sign-in, eero SSO, Guardian VPN and the WireGuard installer.

eero does not publish this API. Everything here describes how eero Desktop uses it, and eero can change the API at any time without notice. eero Desktop is not affiliated with or endorsed by Amazon or eero. Whether a feature works depends on your hardware, your account's role and permissions, and your subscriptions.

**Platforms.** The Windows, Linux and macOS apps send the same requests with the same bodies. The differences are listed here, and everything else in this reference applies to all three:

| | Windows | Linux | macOS |
|---|---|---|---|
| `User-Agent` | `EeroDesktop/1.1.0 (Windows; unofficial)` | `EeroDesktop/1.1.0 (Linux; unofficial)` | `EeroDesktop/1.1.0 (macOS; unofficial)` |
| Remembered session | Windows DPAPI, current user | AES-GCM, key in the Secret Service keyring | AES-GCM, key in the login Keychain |
| Embedded browser | WebView2, private profile | WebKitGTK 6.0, private session | WKWebView, non-persistent session |
| Guardian VPN tunnel | WireGuard for Windows | NetworkManager or `wg-quick` | `wireguard-tools` or the WireGuard app |

## Contents

- [1. Conventions](#1-conventions)
  - [Placeholders](#placeholders)
  - [Encodings](#encodings)
  - [Input rules used throughout](#input-rules-used-throughout)
  - [Refresh after a change](#refresh-after-a-change)
  - [Errors and badges](#errors-and-badges)
- [2. API basics](#2-api-basics)
  - [Base URL and versions](#base-url-and-versions)
  - [Transport](#transport)
  - [Authentication](#authentication)
  - [Response envelope](#response-envelope)
  - [Error handling](#error-handling)
  - [Session refresh](#session-refresh)
- [3. Sign-in, registration and account](#3-sign-in-registration-and-account)
  - [Sign-in and registration](#sign-in-and-registration)
  - [Account creation](#account-creation)
  - [Account, dashboard and notifications](#account-dashboard-and-notifications)
  - [Settings › Account settings](#settings--account-settings)
  - [Settings › Account settings › Account data](#settings--account-settings--account-data)
  - [Settings › Account settings › Trusted certificates](#settings--account-settings--trusted-certificates)
- [4. Page loads](#4-page-loads)
  - [Endpoints loaded when a page opens](#endpoints-loaded-when-a-page-opens)
  - [Network refresh after a change](#network-refresh-after-a-change)
- [5. Home](#5-home)
  - [Home › Internet](#home--internet)
  - [Home › Profiles](#home--profiles)
  - [Home › Profiles › Scheduled pauses](#home--profiles--scheduled-pauses)
  - [Home › Security & privacy](#home--security--privacy)
  - [Home › Security & privacy › Advanced](#home--security--privacy--advanced)
  - [Home › Security & privacy › Identity protection](#home--security--privacy--identity-protection)
  - [Home › Security & privacy › VPN (Guardian)](#home--security--privacy--vpn-guardian)
  - [Home › eeros › Details](#home--eeros--details)
  - [Home › eeros › Details (more actions)](#home--eeros--details-more-actions)
  - [Home › eeros › Placement tests](#home--eeros--placement-tests)
  - [Home › Add or replace eero](#home--add-or-replace-eero)
  - [Home › Add or replace eero › Setup tools](#home--add-or-replace-eero--setup-tools)
  - [Home › Add or replace eero › Create a network](#home--add-or-replace-eero--create-a-network)
- [6. Devices](#6-devices)
  - [Devices › Device details and actions](#devices--device-details-and-actions)
  - [Devices › Blocked devices](#devices--blocked-devices)
- [7. Activity](#7-activity)
  - [Activity › Internet (speed)](#activity--internet-speed)
  - [Activity › Data usage](#activity--data-usage)
  - [Activity › Network events](#activity--network-events)
  - [Activity › Security activity](#activity--security-activity)
  - [Activity › Usage details](#activity--usage-details)
- [8. Settings](#8-settings)
  - [Settings › Wifi name & password](#settings--wifi-name--password)
  - [Settings › Guest wifi network](#settings--guest-wifi-network)
  - [Settings › Nickname & location](#settings--nickname--location)
  - [Settings › Network settings (shared write)](#settings--network-settings-shared-write)
  - [Settings › User permissions](#settings--user-permissions)
  - [Settings › User permissions › Join a network](#settings--user-permissions--join-a-network)
  - [Settings › User permissions › Incoming access invitations](#settings--user-permissions--incoming-access-invitations)
  - [Settings › Software updates](#settings--software-updates)
  - [Settings › Notifications](#settings--notifications)
  - [Settings › Notifications › Windows notifications](#settings--notifications--windows-notifications)
  - [Settings › Troubleshooting](#settings--troubleshooting)
  - [Settings › Troubleshooting › Guided diagnostics](#settings--troubleshooting--guided-diagnostics)
  - [Settings › Troubleshooting › Connect a device](#settings--troubleshooting--connect-a-device)
  - [Settings › Account settings › Subscriptions](#settings--account-settings--subscriptions)
  - [Settings › Account settings › Subscriptions › Offers & referrals](#settings--account-settings--subscriptions--offers--referrals)
  - [Settings › Account settings › Subscriptions › Entitlements & trials](#settings--account-settings--subscriptions--entitlements--trials)
  - [Settings › Account settings › Subscriptions › Partner offers](#settings--account-settings--subscriptions--partner-offers)
  - [Settings › Connected services](#settings--connected-services)
  - [Settings › Connected services › Amazon connected home](#settings--connected-services--amazon-connected-home)
  - [Settings › Connected services › Accessories & cellular](#settings--connected-services--accessories--cellular)
  - [Settings › Network management](#settings--network-management)
  - [Settings › Network management › Check recipient](#settings--network-management--check-recipient)
  - [Settings › Network management › Organization & community](#settings--network-management--organization--community)
  - [Settings › Advanced networking › Wireless settings](#settings--advanced-networking--wireless-settings)
  - [Settings › Advanced networking › Wireless settings › Security by radio band](#settings--advanced-networking--wireless-settings--security-by-radio-band)
  - [Settings › Advanced networking › Wireless settings › Fast roaming](#settings--advanced-networking--wireless-settings--fast-roaming)
  - [Settings › Advanced networking › Thread](#settings--advanced-networking--thread)
  - [Settings › Advanced networking › Dynamic DNS](#settings--advanced-networking--dynamic-dns)
  - [Settings › Advanced networking › Reservations & port forwarding](#settings--advanced-networking--reservations--port-forwarding)
  - [Settings › Advanced networking › Power saving](#settings--advanced-networking--power-saving)
  - [Settings › Advanced networking › Network flags](#settings--advanced-networking--network-flags)
  - [Settings › Advanced networking › Internet backup](#settings--advanced-networking--internet-backup)
  - [Settings › Advanced networking › Internet backup (discovery and order)](#settings--advanced-networking--internet-backup-discovery-and-order)
  - [Settings › Advanced networking › Multi-WAN](#settings--advanced-networking--multi-wan)
  - [Settings › Advanced networking › Internet connection › ISP settings (PPPoE / VLAN)](#settings--advanced-networking--internet-connection--isp-settings-pppoe--vlan)
  - [Settings › Advanced networking › DNS › DNS performance](#settings--advanced-networking--dns--dns-performance)
  - [Settings › Advanced networking › Multiple static IPs](#settings--advanced-networking--multiple-static-ips)
  - [Settings › Additional wifi networks](#settings--additional-wifi-networks)
  - [Settings › Business guest wifi (captive portal)](#settings--business-guest-wifi-captive-portal)
  - [Settings › Business guest wifi › Other networks](#settings--business-guest-wifi--other-networks)
  - [Settings › Business guest wifi › eero Insight](#settings--business-guest-wifi--eero-insight)
- [9. Help, support and chat](#9-help-support-and-chat)
  - [Help › Report an issue](#help--report-an-issue)
  - [Help › Contact support](#help--contact-support)
  - [Home › Chat › Warm-up](#home--chat--warm-up)
  - [Home › Chat (eero assistant)](#home--chat-eero-assistant)
- [10. Billing and payments](#10-billing-and-payments)
  - [Billing and payment hand-off](#billing-and-payment-hand-off)
- [11. Outside services](#11-outside-services)
  - [Sign-in through outside services](#sign-in-through-outside-services)
  - [Embedded browser rules](#embedded-browser-rules)
  - [Guardian VPN](#guardian-vpn)
- [12. Local device interfaces](#12-local-device-interfaces)
  - [Bluetooth and Windows startup (no HTTP)](#bluetooth-and-windows-startup-no-http)
- [13. Known limits of this reference](#13-known-limits-of-this-reference)
- [14. Endpoint index](#14-endpoint-index)

## 1. Conventions

These rules apply to every eero request unless an endpoint says otherwise.

### Placeholders

| Placeholder | Meaning |
|---|---|
| `{network_url}` | The `url` eero returns for the selected network, usually `/2.2/networks/{network_id}`. Paths written `/2.2/networks/{network_id}/…` are built by appending to this URL, so they follow whatever API version it carries. |
| `{network_id}` | The last segment of `{network_url}`. It must match `^[A-Za-z0-9_-]{1,128}$`, or the request is not sent. |
| `{eero_url}`, `{profile_url}`, `{device_url}`, `{schedule_url}` | The `url` field of that object as eero returns it, for example `/2.2/eeros/{eero_id}`, `/2.2/networks/{network_id}/profiles/{profile_id}` or `/2.2/networks/{network_id}/devices/{device_mac}`. |
| `{eero_id}`, `{profile_id}` | The last segment of the object's `url`, with the same identifier rule as `{network_id}`. |
| `{serial}` | An eero serial number, trimmed. It must match `^[A-Za-z0-9-]{8,40}$` and is URL-escaped. |
| `{device_mac}` | A MAC address. The separators `-`, `:`, `.` and spaces are removed, exactly 12 hex digits must remain, and it is sent as lowercase `aa:bb:cc:dd:ee:ff`. |
| `resources.<name>` | The network, eero or profile object can carry a `resources` map. Each value is either a URL string or an object with a `url`. An endpoint described as "resource `x`, fallback `y`" uses `resources.x` when eero provides it, and `y` otherwise. |
| `{start}`, `{end}` | UTC times formatted `yyyy-MM-ddTHH:mm:ssZ` and URL-escaped (`:` becomes `%3A`). `end` is now, and `start` is now minus the chosen number of days. |

### Encodings

| Encoding | Content type | Rules |
|---|---|---|
| JSON | `application/json; charset=utf-8` | The default when a request has a body. |
| form | `application/x-www-form-urlencoded` | Booleans are sent as `true` or `false`. A list repeats its key once per value, for example `colors[]=blue&colors[]=white`. |
| multipart | `multipart/form-data` | Used only by Help › Report an issue. Every text part is `text/plain; charset=utf-8`. |
| none | — | No body and no `Content-Type`. |

### Input rules used throughout

| Rule | Definition |
|---|---|
| Wifi name | 1 to 32 UTF-8 bytes after trimming, not blank. |
| Wifi password | 8 to 63 characters, all printable ASCII (codes 32 to 126). |
| Name | Not blank, at most 100 characters after trimming. |
| Email | Trimmed, must match `^[^\s@]+@[^\s@]+\.[^\s@]+$`. Some fields also limit it to 254 characters; those are noted. |
| Phone | Input must match `^\+?[0-9\s().-]+$`. A 10-digit number without `+` gets a leading `1`. The result must have 8 to 15 digits and cannot start with `0`, and a number starting with `1` must have exactly 11 digits. It is sent in E.164 form, `+<digits>`. |
| IPv4 | A dotted quad matching `^(\d{1,3}\.){3}\d{1,3}$` that parses as IPv4. It is sent in canonical form. |
| Confirmation | A Yes/No dialog the user must accept before the request is sent. |

### Refresh after a change

After a successful change, the app runs a **standard refresh**:

1. It re-reads the network (`GET {network_url}`), followed by the network's devices, eeros and profiles lists. See [Network refresh after a change](#network-refresh-after-a-change).
2. It re-reads whatever is open: the profile (`GET {profile_url}`), the eero (`GET {eero_url}`), or the current page's load request from [the page table](#endpoints-loaded-when-a-page-opens).

Pausing a device and renaming a device use a **lists-only refresh**, which re-reads only the network lists. Endpoints marked "no refresh" skip both.

### Errors and badges

When a page fails to load, the app remembers the result for that network and page, and shows it as a badge on the menu entry until a later load succeeds:

| HTTP status | Badge |
|---|---|
| 402 | "Needs a plan upgrade" |
| 401, 403 | "Not available on your account" |
| 404 | "Not available on this network" |

Some badges are worked out from data the app already has, so no request is made:

| Badge | Pages | Condition |
|---|---|---|
| "Not in bridge mode" | Home › Profiles; Settings › Advanced networking › Reservations & port forwarding | The network's `connection.mode` is `bridged` or `ipPassthrough`. |
| "Business networks only" | Settings › Business guest wifi; Settings › Business guest wifi › eero Insight; Settings › Account settings › Subscriptions › Business billing | `network_customer_type` (or `network_type`) contains `resid`, ignoring case. |
| "Needs eero Plus" | Activity › Security activity; Settings › Advanced networking › Dynamic DNS; Home › Security & privacy › Identity protection; Settings › Account settings › Subscriptions › Partner benefits; Home › Security & privacy › VPN | `premium_status` (a string, or its `status` or `state` field) is `inactive`, `none`, `expired`, `canceled`, `cancelled`, `not_subscribed`, `unsubscribed`, `free` or `lapsed`. |

**Preview mode** (`--preview`) uses demonstration data. It sends no requests and refuses every change locally.

## 2. API basics

### Base URL and versions

| Item | Value |
|---|---|
| Origin | `https://api-user.e2ro.com` (HTTPS, port 443 only) |
| Default version | `/2.2/` |
| Other allowed versions | `/2.3/`, `/2.4/` |
| Path resolution | A relative path such as `account` resolves to `https://api-user.e2ro.com/2.2/account`. A path that starts with `/` (for example `/2.3/networks/123`) is resolved against the origin as-is. An absolute `https://` URL is accepted only if it passes the checks below. |

**Resource URL validation.** This applies to every eero request and to URLs the server returns, such as a network `url` or `resources.*`. A URL is rejected as "Untrusted API resource URL" unless all of these hold:

- the scheme is `https`. Any `http:` value, or any other value containing `://` that does not start with `https://`, is rejected before parsing;
- the host is exactly `api-user.e2ro.com` and the port is `443`;
- there is no user-info part (`user@`) and no fragment (`#...`);
- the absolute path starts with `/2.2/`, `/2.3/` or `/2.4/` after normalization, so `/2.2/../../outside` is rejected.

Empty or whitespace paths are rejected ("The server did not provide a resource URL.").

### Transport

| Setting | Value |
|---|---|
| `User-Agent` | `EeroDesktop/1.1.0 (Windows; unofficial)` |
| `Accept` | `application/json` |
| Timeout | 30 s per request |
| Redirects | Not followed (auto-redirect disabled) |
| Cookies | No cookie jar. The session cookie is set by hand on each request. |
| Compression | gzip and deflate accepted |
| TLS | TLS 1.2 is enabled explicitly |
| Concurrency | Requests are serialized: one eero API call at a time. A chat stream holds this lock only until its response headers arrive, so reading a long reply does not block other calls. |

### Authentication

- Session cookie: `Cookie: s=<session token>`.
- Token rules, applied to every token sent or received: 1 to 8192 characters, printable ASCII only (code points 33 to 126), with no `;` or `,`. A token that starts with `eero-web-` is a retired web-preview credential. The app refuses it and asks for a fresh email or phone sign-in.
- New session cookies are read from every `Set-Cookie` response header with the pattern `(?:^|,\s*)s=([^; ,]+)`. When one is present it replaces the current session, which is the rotating-cookie behavior.
- Storage: if "Remember me" is on, the session token is stored encrypted with Windows DPAPI for the current user. If it is off, nothing is written to disk.

### Response envelope

```json
{ "meta": { "code": 200, "error": "error.some.key" }, "data": { ... } }
```

- Only `data` is returned to the caller.
- Effective status: if `meta.code` parses as an integer it is used, otherwise the HTTP status. A request fails when the HTTP status is not 2xx, or when `meta.code` is outside 200 to 299 even though HTTP returned 2xx.
- Error code reported: the HTTP status if HTTP failed, otherwise `meta.code`. The error key is `meta.error`.
- A non-JSON body with a 2xx status is an error ("eero returned an unexpected response"). A non-JSON body on an error status is ignored, and the HTTP code is used.
- An empty body is allowed. `data` is then null.

### Error handling

| Code | Meaning shown to user |
|---|---|
| 401, 419 | Code or session not accepted. For authenticated calls this is flagged as **session expired**, and the app returns to sign-in. |
| 402 | Feature requires an active eero subscription |
| 403 | Account lacks permission |
| 404, 405 | Feature not available for this network or API version |
| 429 | eero is rate-limiting. **No automatic retry**: the user is asked to wait a few minutes. |
| 5xx | eero service could not complete the request |
| other | "eero rejected the request (code)" |

The `meta.error` key is appended in parentheses only when it matches `^[a-zA-Z0-9_.-]{1,140}$`.

### Session refresh

On an authenticated call that fails with **HTTP 401 and `meta.error` = `error.session.refresh`**:

1. `POST /2.2/login/refresh` is sent once (no body, current cookie).
2. The new session is the `Set-Cookie` `s=` value, else `data.user_token`, else the current token is kept.
3. The original request is retried once with the new session.

Any other 401 or 419 is treated as an expired session, with no retry. No other errors are retried.

## 3. Sign-in, registration and account

### Sign-in and registration

#### GET /2.2/account (connection check)

Unauthenticated probe behind "Trouble connecting? Check connection" and the diagnostic connection test.

| Item | Detail |
|---|---|
| Auth | **None** (no cookie) |
| Expected result | HTTP 401. This counts as success: "Connected to eero securely." |
| A 2xx reply | Treated as an unexpected response (error) |
| Other errors | Shown, with transport failures sorted into categories: `network_access_blocked` (Windows socket error 10013), `dns_failure`, `tls_failure`, `timeout`, `connection_refused`, `network_unreachable`, `connection_failure` |

No login or verification-code request is made.

#### POST /2.2/login

Starts email or phone sign-in by sending a verification code.

| Item | Detail |
|---|---|
| Auth | None. Any saved session is cleared first. |
| Encoding | form |

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `login` | string | Email, or phone in E.164 form (`+` and 8 to 15 digits) | Input is at most 254 characters. Email must match `^[^\s@]+@[^\s@]+\.[^\s@]+$`. Phone rules are below. |

**Phone normalization** (when "Phone number" is chosen):

- The input may contain only digits, spaces, `(`, `)`, `.`, `-` and an optional leading `+`.
- Without `+`: 10 digits get the prefix `1`. An 11-digit number must start with `1`. Anything else is rejected.
- The result must have 8 to 15 digits and must not start with `0`.
- A number starting with `1` must have exactly 11 digits.
- The value sent is `+` followed by the digits, for example `+12025550123`. The display format is `+1 (202) 555-0123`.

| Response field | Use |
|---|---|
| `data.user_token` | Required. Becomes the pending verification credential and must pass the token rules. |

After the request, the client blocks "Resend code" for 60 s.

#### POST /2.2/login/verify

| Item | Detail |
|---|---|
| Auth | `Cookie: s=<pending user_token>` |
| Encoding | form |
| Gating | Requires a pending login that is not a registration |

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `code` | string | `^[0-9]{4,10}$` after trimming | Input box allows 12 characters |

The session is taken from the first available of: `Set-Cookie s=`, `data.user_token`, `data.session_token`, the pending token. A wrong code (for example 401 `error.code.invalid`) keeps the pending challenge so the user can retry. After success the app loads the account.

#### POST /2.2/login/resend

| Item | Detail |
|---|---|
| Auth | `Cookie: s=<pending user_token>` |
| Body | None |
| Gating | Pending login. The client allows one request per 60 s. |
| Response | If `Set-Cookie s=` or `data.user_token` is present, it replaces the pending token. |

#### POST /2.2/login/refresh

See Session refresh above. No body. Uses the current session cookie. Reads `Set-Cookie s=` or `data.user_token`.

#### POST /2.2/login/amazon

Exchanges an Amazon access token for an eero session. The Amazon steps are under [Outside services](#11-outside-services).

| Item | Detail |
|---|---|
| Auth | None |
| Encoding | form |

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `auth_token` | string | Amazon `access_token`; must pass the token rules | |
| `create_account` | boolean | Always `false` | Never creates an eero account |

| Response field | Use |
|---|---|
| `Set-Cookie s=` or `data.user_token` | Candidate session. It is not committed yet. |

The candidate is then verified with `GET /2.2/account` using `Cookie: s=<candidate>`. The response must include a non-empty `user_id`, `id` or `url`, or an `email` key. Otherwise the sign-in is refused and any earlier saved session stays in place. Only after verification is the session (rotated cookie if one was sent) committed.

| Known error | Handling |
|---|---|
| `error.login.unknown` | The Amazon account is not linked to an existing eero login. The user is told to use email or phone instead. Nothing is created or changed. |

#### GET /2.2/consents/account?country={country}

Loads the consent list for account creation.

| Item | Detail |
|---|---|
| Auth | None |

| Query param | Type | Values / rules |
|---|---|---|
| `country` | string | Two-letter code, `^[A-Z]{2}$` after trimming and upper-casing. Default `US`. URL-escaped. |

| Response field (array items) | Use |
|---|---|
| `name` | Consent key (items without one are skipped) |
| `localized_consent` | Text shown next to the checkbox (falls back to a humanized `name`) |
| `requires_consent` | If true, the box must be ticked before continuing |

If the list is empty, a single "I agree to eero's terms and privacy policy" box is required instead. All boxes start unticked. A link to `https://www.eero.com/legal` is shown.

#### POST /2.2/register

| Item | Detail |
|---|---|
| Auth | None. Any saved session is cleared first. |
| Encoding | form |
| Gating | Required consents ticked, then the user confirms |

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `name` | string | 1 to 100 characters after trimming | |
| `email` | string | `^[^\s@]+@[^\s@]+\.[^\s@]+$` | |
| `phone` | string | E.164, normalized as for login | Phone or SMS verification is required |
| `send_verification` | boolean | Always `true` | |

| Response field | Use |
|---|---|
| `data.user_token` | Required. Becomes the pending registration credential. |

#### POST /2.2/register/verify

| Item | Detail |
|---|---|
| Auth | `Cookie: s=<pending registration token>` |
| Encoding | **JSON** |
| Gating | Pending registration. A registration code is never sent to `login/verify`. |

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `code` | string | `^[0-9]{4,10}$` | |
| `country` | string | The two-letter code used for consents | |
| `consents` | object | `{ "<consent name>": true \| false, ... }` | One key per consent returned by `consents/account`. Optional consents may be `false`. |

Example: `{"code":"123456","country":"US","consents":{"terms":true}}`

The session is taken as for `login/verify`.

#### POST /2.2/register/phone/resend

| Item | Detail |
|---|---|
| Auth | `Cookie: s=<pending registration token>` |
| Body | None |
| Response | Optional `Set-Cookie s=` or `data.user_token` replaces the pending token |

#### POST /2.2/logout

| Item | Detail |
|---|---|
| Auth | Session cookie (refresh rule applies) |
| Body | None |
| Behavior | Sent only when signed in. All errors (API, network, timeout) are ignored, and the local session is always cleared. |

### Account creation

Started from the sign-in screen (**New to eero? Create an account**). These calls use no session cookie until a pending token is issued.

#### GET /2.2/consents/account
Query: `country=<CC>` — trimmed, upper-cased, `^[A-Z]{2}$`, default `US`. No authentication. Response: a list of agreements.

| Field | Type | Notes |
|---|---|---|
| `name` | string | Consent key (entries without a name are skipped) |
| `localized_consent` | string | Text shown next to the checkbox (falls back to a label made from `name`) |
| `requires_consent` | boolean | If `true`, the box must be ticked to continue |

All consent boxes start unchecked. If the list is empty, a single "I have reviewed and agree to eero's terms and privacy policy" box must be ticked instead (it is not sent anywhere).

### Account, dashboard and notifications

#### GET /2.2/account

Loads the signed-in account. It is also used to verify sessions imported from a browser or provider sign-in (see [Outside services](#11-outside-services)).

| Response field | Use |
|---|---|
| `networks` | Network list, either an array or `{ "data": [...] }`. The first entry's `url` becomes the selected network. |
| `name`, `email`, `phone` | Identity shown for confirmation |
| `user_id`, `id`, `url` | Identity check (Amazon sign-in) and per-account scoping of notifications |
| `log_id` | Preferred input for the per-account VPN profile scope (falls back to `url`, `id`, `email`) |

#### GET /2.2/networks

Fallback used only when `account.networks` is empty. The response is a list of networks, each with `url` and `name`.

#### GET {network url}

`{network url}` is the network's `url`, for example `/2.2/networks/{network_id}`. The network ID used by other endpoints is the last path segment of this URL and must match `^[A-Za-z0-9_-]{1,128}$`.

| Response field | Use |
|---|---|
| `resources.devices`, `resources.eeros`, `resources.profiles`, `resources.speedtest` | Follow-up URLs. Each may be a string or `{ "url": ... }`. |
| `name`, `speed.down.value`, `speed.down.units`, `speed.up.value`, `speed.up.units` | Dashboard display. Units default to `Mbps`. |

#### GET {resources.devices} (default `{network url}/devices`)

#### GET {resources.eeros} (default `{network url}/eeros`)

#### GET {resources.profiles} (default `{network url}/profiles`)

Lists loaded after every network load. If one fails with a non-session error, only that section shows "Could not load …". A session expiry ends the session.

Device fields read: `nickname`, `display_name`, `hostname`, `mac`, `blocked`, `paused`, `connected`, `ip`, `ip_address`, `wireless`, `frequency`, `profile.name`, `url`.

**Auto refresh**: the network and the three lists are reloaded every **60 s** only while the window is active, the user is signed in, no other action is running, and the Home or Devices page is open.

#### POST {resources.speedtest} (default `{network url}/speedtest`)

| Item | Detail |
|---|---|
| Body | None |
| Response | 2xx, for example 202. Results arrive later on the Activity page. |

#### GET {network url}/notifications_history?limit=50

Background polling for Windows alerts.

| Query param | Value |
|---|---|
| `limit` | `50` |

| Gating | Detail |
|---|---|
| Enabled | Only when "Windows alerts" is on, the user is signed in, a network is selected and the app is not busy |
| Interval | Once at sign-in or session restore, then every **1 minute** while the app runs (including from the notification area when "Keep running when closed" or "Start with Windows" is on) |
| Networks | The selected network only, or every network on the account when "Monitor all networks" is on |

| Response field | Use |
|---|---|
| Top-level array, or `notifications`, or `items` | Entries, newest first. At most 50 are considered. |
| `uuid` (falls back to `id`) | De-duplication key: 1 to 256 characters, no control characters |
| `title` | Balloon title, trimmed to 63 characters (several new items are shown as "N eero network updates") |
| `body` | Balloon text, prefixed with the network name and trimmed to 240 characters |

The first poll for an account and network pair only records existing IDs and shows no alerts. The seen-ID journal holds up to 256 IDs per pair and 64 pairs, stored encrypted per Windows user. A non-session error for one network is skipped silently. A session expiry signs the user out and shows a "needs sign-in" balloon.

### Settings › Account settings

#### GET /2.2/account (Account settings)
Page data (account object). Fields read on this and related pages:

| Field | Type | Notes |
|---|---|---|
| `name` | string | |
| `email` | string or object | If an object, `value` / `email` / `phone` / `number` |
| `phone` | string or object | Same as above |
| `auth.type` | string | `eero` (email or text code), `amazon`, `sso` (internet provider) |
| `role` | string | Shown if present |
| `consents.marketing_emails` | boolean | Marketing email switch |
| `push_settings` | object of booleans | Account alerts (see Notifications) |
| `premium_status` | string or object (`status` / `state`) | Used when the network has none |
| `premium_details` | object (`tier` / `plan` / `name`) | Used when the network has none |

#### PUT /2.2/account/name
**Profile › Name › Change**. Body: **form**.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `name` | string | Name rule; trimmed | |

#### PUT /2.2/account/email
**Profile › Email › Change**. Body: **form**. Starts an email change; eero sends a code.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `email` | string | Email rule; trimmed | New address |

#### POST /2.2/account/email/verify
**Profile › Email › Verify**. Body: **form**.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `code` | string | `^[0-9]{4,10}$` after trimming | Code sent to the new address |

#### PUT /2.2/account/phone
**Profile › Phone › Change**. Body: **form**.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `phone` | string | Phone rule, normalized to `+<digits>` | New number; eero texts a code |

#### POST /2.2/account/phone/verify
**Profile › Phone › Verify**. Body: **form**. Field `code`: `^[0-9]{4,10}$`.

#### PUT /2.2/account/consents
**Email preferences › eero marketing emails** switch. Body: **form**.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `marketing_emails` | boolean | `true` / `false` | Account and security emails are always sent |

#### DELETE /2.2/account
**Danger zone › Delete account**. The user must type exactly `DELETE MY EERO ACCOUNT` and then confirm a second prompt. Body: none. On success the client discards the session token locally, stops background refresh, clears all data and returns to the sign-in screen. Not available in preview mode.

### Settings › Account settings › Account data

Page reads `account.email.verified` (bool) and `account.is_external` (bool).

#### POST /2.2/register/email/resend
"Resend email" — shown only when `account.email.verified` is not `true`. Encoding: **JSON**, body is the empty JSON string `""`. No refresh; shows "Verification email requested."

#### GET /2.2/data-controller/info
"Data controller" › View. Response shown as-is.

#### GET /2.2/countries/sms
"Countries with text sign-in" › View. Response shown as-is.

#### GET /2.2/app_configuration[?networkId={network_id}]
"App configuration" › View. `networkId` added when a network is selected.

#### GET /2.2/directedid?d=0&i=0&p=0
"Amazon account ID" › View. Query values are fixed at `0` (each would be allowed 0–99).

### Settings › Account settings › Trusted certificates

#### GET /2.2/account/trust
Loads the page. Response: array or `{ certificates: [...] }` / `{ trust: [...] }`. Item fields read: `serial` / `certificate_serial` (items without one are skipped), `name` / `subject` / `common_name`, `not_after` / `expires_at` / `expiration`.

#### POST /2.2/account/trust
Add a certificate. Encoding: **form**.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `certificate` | string | CRLF normalised to LF, trimmed; ≤ 16384 chars; must match `^-----BEGIN CERTIFICATE-----\n[A-Za-z0-9+/=\n]+\n-----END CERTIFICATE-----$` (exactly one PEM certificate; private keys rejected) | |

#### PUT /2.2/account/trust
Replace a certificate. Encoding: **form**.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `certificate` | string | Same PEM rules | |
| `serial` | string | Serial of the certificate being replaced; `^[A-Za-z0-9_.:-]{1,128}$` | e.g. `AB:CD` |

#### DELETE /2.2/account/trust/{certificate_serial}
Remove. Serial checked against `^[A-Za-z0-9_.:-]{1,128}$` and URL-escaped. Body **none**. Confirmation: "Remove trusted certificate <serial>?"

## 4. Page loads

### Endpoints loaded when a page opens

All loads are `GET`. "Network object" means the page reads only the already-loaded `GET {network_url}` data and sends no request of its own when it opens.

| Menu path | Endpoint(s) loaded on open | Query |
|---|---|---|
| Home › Internet | resource `speedtest`, fallback `{network_url}/speedtest` | — |
| Home › eeros | none (uses the eeros list from Network refresh) | — |
| Home › eeros › Details (one eero) | `{eero_url}` | — |
| Home › eeros › Placement tests | none (uses the eeros list) | — |
| Home › Profiles | none (uses the profiles list) | — |
| Home › Profiles › Profile (one profile) | `{profile_url}` (also fetched by the **Manage** button) | — |
| Home › Security & privacy | `{network_url}/dns_policies/network` | — |
| Home › Security & privacy › Content filters | `{network_url}/subnets_config` | — |
| Home › Security & privacy › VPN | none on open. **Check** sends `GET /2.2/partners/guardian/networks/{network_id}/availability` | — |
| Home › Security & privacy › Identity protection | `/2.2/account/identity_protection` | — |
| Home › Add or replace eero | none | — |
| Home › Add or replace eero › Find nearby eeros | none (Bluetooth only) | — |
| Home › Add or replace eero › Automatic setup | `{network_url}/auto_discovered_eeros` | — |
| Home › Add or replace eero › Create a network | none on open | — |
| Home › Add or replace eero › Setup tools | none on open | — |
| Home › Chat | none on open | — |
| Devices › Blocked devices | `{network_url}/blacklist` | — |
| Activity › Data usage | `{network_url}/data_usage` | `start`, `end`, `cadence=daily` (period = the chosen number of days, default 7) |
| Activity › Security activity | `{network_url}/insights` | `start`, `end` (always the last 7 days), `cadence=daily` |
| Activity › Network events | `{network_url}/app_events` | `page_size=50` |
| Activity › Usage details | none on open | — |
| Settings › Wifi name & password | network object | — |
| Settings › Guest wifi network | resource `guestnetwork`, fallback `{network_url}/guestnetwork` | — |
| Settings › Additional wifi networks | `{network_url}/subnets_config` | — |
| Settings › Business guest wifi | `{network_url}/captive_portal/configurations` | — |
| Settings › Business guest wifi › eero Insight | none on open | — |
| Settings › Nickname & location | network object | — |
| Settings › Advanced networking (menu) | none | — |
| Settings › Advanced networking › Internet connection | network object | — |
| Settings › Advanced networking › Internet connection › ISP settings | network object | — |
| Settings › Advanced networking › DHCP & NAT | network object | — |
| Settings › Advanced networking › DNS | network object | — |
| Settings › Advanced networking › DNS › DNS performance | `{network_url}/dns_perf_test/{action_uuid}`, only after a test was started in this session (`action_uuid` comes from the start response) | — |
| Settings › Advanced networking › Reservations & port forwarding | resource `routing`, fallback `{network_url}/routing` | — |
| Settings › Advanced networking › IPv6 | network object (`ipv6_upstream`) | — |
| Settings › Advanced networking › UPnP | network object (`upnp`) | — |
| Settings › Advanced networking › Wireless settings | network object | — |
| Settings › Advanced networking › Wireless settings › Fast roaming | `{network_url}/fast_transition` | — |
| Settings › Advanced networking › Thread | resource `thread`, fallback `{network_url}/thread` | — |
| Settings › Advanced networking › Dynamic DNS | network object | — |
| Settings › Advanced networking › Power saving | `{network_url}/power_saving/schedules` | — |
| Settings › Advanced networking › Internet backup | `{network_url}/backupinternet` and `{network_url}/backup_access_points` (combined as `status` / `access_points`) | — |
| Settings › Advanced networking › Internet backup › Find backup wifi | `{network_url}/backup_access_points/ssid_discovery` | — |
| Settings › Advanced networking › Multi-WAN | `{network_url}/wan/links` | — |
| Settings › Advanced networking › Multiple static IPs | `/2.3/networks/{last segment of network_url}/multistaticip` (this segment is not checked against the identifier pattern) | — |
| Settings › Advanced networking › Network flags | none on open | — |
| Settings › User permissions | `{network_url}/members` and `{network_url}/invites` (combined as `members` / `invites`) | — |
| Settings › User permissions › Join a network | none on open | — |
| Settings › User permissions › Incoming access invitations | `/2.2/incoming_member_promotions` | — |
| Settings › Software updates | resource `updates`, fallback `{network_url}/updates` | — |
| Settings › Appearance | none (local preference only) | — |
| Settings › Notifications | `{network_url}/notifications` | — |
| Settings › Notifications › Windows notifications | none on open (background polling: see Windows notifications) | — |
| Settings › Troubleshooting | none | — |
| Settings › Troubleshooting › Guided diagnostics | resource `diagnostics`, fallback `{network_url}/diagnostics` | — |
| Settings › Troubleshooting › Connect a device | network object (`temporary_flags.hide_5g`) | — |
| Settings › Account settings | `/2.2/account` (relative `account`; this also replaces the cached account) | — |
| Settings › Account settings › Subscriptions | none | — |
| Settings › Account settings › Subscriptions › Redeem a license key | none on open | — |
| Settings › Account settings › Subscriptions › Partner benefits | none on open | — |
| Settings › Account settings › Subscriptions › Manage subscriptions & purchases | none on open | — |
| Settings › Account settings › Subscriptions › Offers & referrals | `/2.4/premium/plans/{network_id}` (only when a network is selected) | — |
| Settings › Account settings › Subscriptions › Billing & status | `/2.2/premium/customer` | — |
| Settings › Account settings › Subscriptions › Business billing | none on open | — |
| Settings › Account settings › Subscriptions › Entitlements & trials | `/2.2/entitlements/networks/{network_id}/features` and `/2.2/entitlements/networks/{network_id}/upsell_features` | — |
| Settings › Account settings › Subscriptions › Partner offers | `/2.2/partners/upsells/offers` and `/2.2/partners/upsells/authentication_methods` | `opt_network_id={network_id}` on the offers call when a network is selected |
| Settings › Account settings › Trusted certificates | `/2.2/account/trust` | — |
| Settings › Account settings › Account data | none on open | — |
| Settings › Connected services | `{network_url}/proxied_nodes` | — |
| Settings › Connected services › Connected device setup | `{network_url}/simple_setup/vendors` | — |
| Settings › Connected services › Amazon connected home | network object (`amazon_account_linked`, `alexa_skill`) | — |
| Settings › Connected services › Accessories & cellular | `{network_url}/accessories/setup/status` (plus network object `accessories`, `cellular_backup`) | — |
| Settings › Network management | none | — |
| Settings › Network management › Pending transfers | `/2.2/network_transfers/pending` | — |
| Settings › Network management › Outgoing transfer | resource `transfer`, fallback `{network_url}/transfer` | — |
| Settings › Network management › Organization & community | none on open | — |
| Settings › About | none | — |
| Help (menu) | none | — |
| Help › Support history | none on open | — |
| Help › Contact support | `/2.2/account/eero_support` | — |
| Help › Report an issue | none on open | — |
| Help › Service availability | none | — |

#### Time-range query format (`start` / `end`)

- Both values are UTC, formatted `yyyy-MM-ddTHH:mm:ssZ` and URL-escaped (`:` becomes `%3A`).
- `end` is now and `start` is now minus N days.
- Example: `?start=2026-10-31T14%3A00%3A00Z&end=2026-11-07T14%3A00%3A00Z&cadence=daily`.

#### Chart responses (Data usage, Security activity, per-device and per-eero usage)

The app reads `series[]`. It draws up to 12 series, with up to 60 values each.

| Field | Type | Use |
|---|---|---|
| `series[].insight_type_name` / `type` / `insight_type` | string | Series title, taken from the first non-empty of these. `download` shows as "Downloaded" and `upload` as "Uploaded". Security series are titled "<Type> blocked". |
| `series[].sum` | number | Total. When missing, the app adds up the `values`. |
| `series[].values[].time` | date/time string | Bar label, and the source of "Busiest day" / "Most on". |
| `series[].values[].value` | number | Bar height. Bytes for usage, counts for security. |

### Network refresh after a change

#### GET {network_url}
- **Used by:** the initial load, network switching and every refresh after a write.
- **Response read** (by the features below):
  - `resources.*`
  - `connection.mode`
  - `premium_status`
  - `network_customer_type` / `network_type`
  - `temporary_flags.hide_5g`
  - `amazon_account_linked`
  - `alexa_skill`
  - `auto_setup_mode`
  - `accessories[]`
  - `cellular_backup`
  - `speed.down.value`, `speed.down.units`, `speed.up.value`, `speed.up.units` (units default to "Mbps")
  - `name`

#### GET resource `devices` (fallback `{network_url}/devices`)
#### GET resource `eeros` (fallback `{network_url}/eeros`)
#### GET resource `profiles` (fallback `{network_url}/profiles`)
- **Encoding:** none.
- **Response:** each one is a list.
  - If one of the three fails (API error, HTTP failure or timeout), the app shows an inline "couldn't load" note for that list and uses an empty list. Session expiry still signs you out.
- **Fields read from devices:**
  - `url`, `mac`
  - `nickname` / `display_name` / `hostname` (display name, first non-empty, falling back to `mac`)
  - `blocked`, `paused`, `connected`
  - `ip` / `ip_address`
  - `wireless`, `frequency`
  - `profile.name`
  - `manufacturer`
  - `source.location`
  - `device_type`
  - `secondary_wan_deny_access`
- **Fields read from eeros:** `url`, `serial`, `model`, `location`, `gateway`.
- **Fields read from profiles:** `url`, `name`, `paused`.

## 5. Home

### Home › Internet

#### POST resource `speedtest` (fallback `{network_url}/speedtest`)
- **Used by:** Activity overview › **Run speed test**.
- **Encoding:** none (no body).
- **Response:** may be empty. HTTP 202 is accepted.
  - The app shows "Speed test started. Results can take a few minutes."
  - This call does not trigger a refresh.

#### GET resource `speedtest` (fallback `{network_url}/speedtest`)
- **Used by:** Home › Internet page load (speed test history).

### Home › Profiles

All profile writes use the standard refresh. While a profile is open, that means re-fetching the network lists and then `GET {profile_url}`.

**Gating:** the page shows "Family profiles are unavailable" and offers no actions when network `connection.mode` is `"bridged"` or `"ipPassthrough"`.

#### POST resource `profiles` (fallback `{network_url}/profiles`)
- **Used by:** Home › Profiles › **Add a profile**.
- **Encoding:** JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `name` | string | Required. After trimming: 1–100 characters; whitespace alone is rejected. Text box limit 254. | Trimmed |
| `paused` | boolean | always `false` | |
| `devices` | array | always `[]` | |

#### GET {profile_url}
- **Used by:** the **Manage** button on a profile card, and the Profile page load and refresh.
- **Response read:**
  - `name`
  - `paused`
  - `devices[]` (`url`, `mac`, name fields, status fields)
  - `schedule[]` (`name`, `start`, `end`, `days[]`, `enabled`, `url`)
  - `resources.schedules`
  - `premium_dns` (shown under "Filter details")
  - `premium_dns.blocked_applications[]`
  - `premium_dns.ad_block_settings.enabled`

#### PUT {profile_url}: pause / resume
- **Used by:** the Internet switch on a profile card (Home › Profiles), and Home › Profiles › Profile › **Internet access**.
- **Confirmation:** "Resume/Pause internet for every device in <name>?"
- **Encoding:** JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `paused` | boolean | `true` = pause, `false` = resume | Only field sent |

#### PUT {profile_url}: rename
- **Used by:** Profile › **Profile name** › **Rename**.
- **Encoding:** JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `name` | string | 1–100 characters after trimming | Only field sent |

#### PUT {profile_url}: assign devices
- **Used by:** Profile › Devices › **Choose devices** › **Save device assignments**.
- **Encoding:** JSON.
- **Behavior:** this replaces the whole membership list. Only checked devices are sent.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `devices` | array of objects | Each entry is `{ "url": "<device url>" }` | The choices are every network device plus the profile's current devices, de-duplicated by `url`. Devices without a `url` are skipped. |

#### DELETE {profile_url}
- **Used by:** Profile › Danger zone › **Delete profile**.
- **Encoding:** none.
- **Confirmation:** you must type the exact profile name. A mismatch gives "The profile name does not match."
- **Afterwards:** the app goes back to Home › Profiles. Devices stay on the network.

#### POST resource `schedules` of the profile (fallback `{profile_url}/schedules`): add scheduled pause
#### PUT {schedule_url}: edit scheduled pause
- **Used by:** Profile › Scheduled pauses › **Add** (POST) or **Edit** (PUT, sent to the schedule item's `url`).
- **Encoding:** JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `name` | string | Required, 1–100 characters after trimming | Trimmed |
| `days` | array of string | One or more of `"SUN"`, `"MON"`, `"TUE"`, `"WED"`, `"THU"`, `"FRI"`, `"SAT"`. At least one is required; duplicates are removed. | Error: "Choose at least one day." |
| `start` | string | 24-hour `HH:mm`, re-formatted as `HH:mm`. Default `"22:00"`. | |
| `end` | string | 24-hour `HH:mm`. Default `"07:00"`. Must differ from `start`; an end earlier than the start continues into the next day. | Times follow the network timezone |
| `enabled` | boolean | Default `true` for a new schedule | |

#### DELETE {schedule_url}
- **Used by:** Profile › Scheduled pauses › **Delete**.
- **Confirmation:** "Delete this scheduled pause?"
- **Encoding:** none.

#### GET {network_url}/dns_policies/profiles/{profile_id}
#### POST {network_url}/dns_policies/profiles/{profile_id}
- **Used by:** Profile › Filters & app controls › **Content filters** › **Change**.
- **Behavior:** the GET loads the current settings and the POST saves them.
- **POST encoding:** form.
- **Response read (GET):**
  - `dns_policies` (a map of booleans)
  - `unified_content_filters.dns_policies` (a map of booleans). Its entries override and add to `dns_policies`.
  - If neither is present, the top-level map itself is read.
- **Which switches appear:**
  - Every boolean key from the list below.
  - When `unified_content_filters` is present, every boolean key that it or `dns_policies` holds.
- **Body:** only the keys you changed, each `<key>=true|false`. If nothing changed, nothing is sent.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `ad_block` | boolean | `true`/`false` | Block ads |
| `block_illegal_content` | boolean | | |
| `block_malware` | boolean | | Block threats |
| `block_pornographic_content` | boolean | | Block adult content |
| `block_violent_content` | boolean | | |
| `safe_search_enabled` | boolean | | SafeSearch |
| `youtube_restricted` | boolean | | YouTube Restricted Mode |
| `block_messaging_content` | boolean | | |
| `block_social_content` | boolean | | |
| `block_gaming_content` | boolean | | |
| `block_streaming_content` | boolean | | |
| `block_shopping_content` | boolean | | |
| *(other boolean keys)* | boolean | Only when `unified_content_filters` is present | Sent under the server's own key |

#### POST {network_url}/dns_policies/profiles/{profile_id}/adblock
- **Used by:** Profile › Filters & app controls › **Ad blocking** switch.
- **Encoding:** JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `enable` | boolean | `true` / `false` | The current state is read from `premium_dns.ad_block_settings.enabled` |

#### GET {network_url}/dns_policies/profiles/{profile_id}/applications
#### PUT {network_url}/dns_policies/profiles/{profile_id}/applications/blocked
- **Used by:** Profile › Filters & app controls › **Block apps** › **Choose**.
- **Response read (GET):** the list is the response itself if it is an array, otherwise `applications`, otherwise `data`. For each app:
  - **ID:** the first non-empty of `id`, `slug`, `name`.
  - **Label:** the first non-empty of `display_name`, `name`, `slug`.
  - **Pre-checked** when the ID is in the profile's `premium_dns.blocked_applications`, or the app's `is_blocked` is true.
  - If the list is empty, or no app has an ID, the raw response is shown read-only and nothing is sent.
- **PUT encoding:** JSON. This replaces the whole blocked set.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `applications` | array of string | The IDs of the checked apps; may be `[]` | |

### Home › Profiles › Scheduled pauses

Same editor as power schedules but with `start`/`end` field names.

#### POST /2.2/networks/{network_id}/profiles/{profile_id}/schedules
URL = `profile.resources.schedules` or `<profile url>/schedules`.

#### PUT {pause_schedule_url}
URL = the schedule item's `url` (from `profile.schedule[]`).

Encoding: **JSON**. No confirmation.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `name` | string | Trimmed; 1–100 characters | |
| `days` | string[] | Subset of `SUN`…`SAT`; at least one | |
| `start` | string | `HH:mm` | Default `22:00` |
| `end` | string | `HH:mm`; different from `start` | Default `07:00` |
| `enabled` | bool | Default `true` | |

Example: `{"name":"Bedtime","days":["MON","TUE"],"start":"22:00","end":"07:00","enabled":true}`

### Home › Security & privacy

#### GET {network_url}/dns_policies/network
- **Used by:** the page load.
- **Response read:**
  - `dns_policies` (or the top-level map): the boolean keys listed below.
  - `ad_block_settings.enabled` and `ad_block_settings.profiles`.
- **When no known boolean keys come back:** a single "Network protection" row opens the editor described under POST below. The editor uses form encoding and sends only changed keys, as for profiles.

#### POST {network_url}/dns_policies/network
- **Used by:** Home › Security & privacy › Protection for the whole network. There is one switch per policy present in the response.
- **Encoding:** form.
- **Body:** each switch sends one field, `<key>=true|false`. The editor sends all changed keys at once.
- **Gating:** some policies need eero Plus. The server refuses them; the app does not check beforehand.

| Field | Type | Label |
|---|---|---|
| `block_malware` | boolean | Block threats |
| `ad_block` | boolean | Block ads |
| `block_illegal_content` | boolean | Block illegal content |
| `block_pornographic_content` | boolean | Block adult content |
| `block_violent_content` | boolean | Block violent content |
| `safe_search_enabled` | boolean | SafeSearch |
| `youtube_restricted` | boolean | YouTube Restricted Mode |
| `block_messaging_content` | boolean | Block messaging apps |
| `block_social_content` | boolean | Block social media |
| `block_gaming_content` | boolean | Block gaming |
| `block_streaming_content` | boolean | Block streaming |
| `block_shopping_content` | boolean | Block shopping |

#### POST {network_url}/dns_policies/adblock
- **Used by:** Home › Security & privacy › Websites & ads › **Ad blocking for profiles** switch. The switch is shown only when the response contains `ad_block_settings`.
- **Encoding:** JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `enable` | boolean | `true` / `false` | |
| `profiles` | array | Copied unchanged from `ad_block_settings.profiles` in the last load | The app never edits this list |

#### GET {network_url}/dns_policies/advanced_content_filter
- **Used by:** Home › Security & privacy › **Block & allow websites** › **Manage**, and Profile › **Block & allow websites** › **Manage**.
- **Response:** shown as read-only details.

#### PUT {network_url}/dns_policies/network/blocked
#### PUT {network_url}/dns_policies/network/allowed
#### PUT {network_url}/dns_policies/profiles/blocked
#### PUT {network_url}/dns_policies/profiles/allowed
- **Used by:** the Website rule dialog, opened with **Add or remove a website rule**.
  - When opened from the network page, `network/...` is used. When opened from a profile, `profiles/...` is used.
  - Rule = **Block** selects `.../blocked` and **Allow** selects `.../allowed`. The default is Block.
- **Encoding:** JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `domain` | string | Trimmed, trailing `.` removed, lower-cased and converted to IDNA ASCII (punycode). It must not contain `://`, `/`, a space or `@`. It must be a valid DNS host name containing a `.`, at most 253 characters. | Example: `Example.COM.` is sent as `example.com` |
| `is_delete` | boolean | `false` adds the rule. `true` removes it (the "Remove this rule instead of adding it" switch). | |
| `profiles` | array of string | `[ "{profile_id}" ]` | Sent only for profile rules |

### Home › Security & privacy › Advanced

#### GET /2.2/networks/{network_id}/safe_filter_levels?safeFilterType={type}
"Filter levels" › Networks / Profiles. Network-relative. `type` is `SubnetFilter` (wifi networks) or `ProfileFilter` (profiles). Response shown as-is.

#### PUT /2.2/networks/{network_id}/dns_policies/network/allowed/cnames
"Allowed alias domains (CNAMEs)" › Whole network. Replaces the list. Network-relative. Encoding: **JSON**.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `domains` | string[] | Input split on newlines and commas; each trimmed, trailing `.` removed, lower-cased, converted to IDNA/punycode; rejects anything containing `://`, `/`, space or `@`; must be a DNS hostname containing a `.`, ≤ 253 chars; duplicates removed; max 100 | An empty list is accepted (clears the list) |

Example: input `Example.com\ncdn.example.com.\nexample.com` → `{"domains":["example.com","cdn.example.com"]}`

#### PUT /2.2/networks/{network_id}/dns_policies/profiles/allowed/cnames
"Allowed alias domains (CNAMEs)" › Profiles. Network-relative. Encoding: **JSON**. Requires at least one profile to exist and be selected.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `domains` | string[] | Same rules as above | |
| `profiles` | string[] | `{profile_id}` values (`^[A-Za-z0-9_-]{1,128}$`), de-duplicated, at least one | |

Example: `{"domains":["example.com"],"profiles":["12","34"]}`

### Home › Security & privacy › Identity protection

#### GET /2.2/account/identity_protection
Page data. Fields read: `enabled` (boolean; when `true` the **Turn on** button is hidden), `status` (string, shown).

#### GET /2.2/networks/{network_id}/identity_protection/countries
Button **Countries**. Displayed as-is (supported countries).

#### POST /2.2/account/identity_protection/signup
Button **Turn on**. Confirmation: "Turn on identity protection for this account in <CC>?". Body: JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `country_code` | string | Trimmed, upper-cased, `^[A-Z]{2}$` | Default `US` |

#### POST /2.2/account/identity_protection/scan
**Scan an email address › Scan email**. Confirmation: "Send <email> to eero's identity-protection service for a scan?". Body: JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `email` | string | Email rule, max 254 characters, trimmed | |

Response (also kept as the "Latest result"): displayed in full. `mwb_full_report_url` — if present, a button opens it in the browser only if it is `https`, port 443, no user-info, host `malwarebytes.com` or a subdomain. `email_unverified` (boolean) — when `true`, a hint to verify the email is shown. No automatic refresh.

#### POST /2.2/account/identity_protection/scan/email
**Verify the email › Send code**. Body: JSON `{"email": "<email>"}` (email rule, max 254). Response not read.

#### POST /2.2/account/identity_protection/verify_email
**Verify the email › Enter code**. Body: JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `code` | string | Trimmed, `^[A-Za-z0-9-]{4,32}$` | Code from the email |

Response is handled like the scan response (stored as the latest result and displayed).

### Home › Security & privacy › VPN (Guardian)

The VPN page checks eligibility, registers this computer with Guardian and connects the tunnel. All of its requests, on eero and on Guardian, are described under [Guardian VPN](#guardian-vpn).

### Home › eeros › Details

#### GET {eero_url}
- **Used by:** the page load and the refresh after any change on this page.
- **Response read:**
  - `model`, `gateway`
  - `ip_address` / `ip`
  - `connected_clients_count`
  - `os_version` / `firmware_version`
  - `serial`
  - `mac_address` / `mac`
  - `location`
  - `led_on`, `led_brightness`
  - `nightlight.enabled`, `nightlight.brightness_percentage`, `nightlight.schedule.enabled`, `nightlight.schedule.on`, `nightlight.schedule.off`
  - `resources.led_action`, `resources.connections`, `resources.reboot`

#### PUT resource `led_action` of the eero
- **Used by:** Details › Lights › **Status light** switch, and **Light brightness** › **Change**.
  - Both controls appear only when `resources.led_action` exists. There is no fallback URL.
- **Encoding:** form.
- **Body:** each control sends only its own field.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `led_on` | boolean | `true` / `false` | Sent by the switch |
| `led_brightness` | integer | 0–100. A non-integer or out-of-range value gives "Enter a brightness from 0 to 100." | Sent by the brightness dialog |

Example: `led_on=true`.

#### PUT {eero_url}/nightlight/settings
- **Used by:** Details › Lights › **Nightlight** › **Change**. The row appears only when the eero object has `nightlight`.
- **Encoding:** JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `enabled` | boolean | | |
| `brightness_percentage` | integer | 0–100 (required) | |
| `schedule` | object | | |
| `schedule.enabled` | boolean | "Use a schedule" | |
| `schedule.on` | string | Must parse as 24-hour `HH:mm`. Default `"20:00"`. | Sent exactly as typed (not re-formatted) |
| `schedule.off` | string | 24-hour `HH:mm`, must differ from `on`. Default `"07:00"`. | The times are checked even when the schedule is off |

#### GET {eero_connections_url}
- **URL:** resource `connections` of the eero. There is no fallback, and the row appears only when the resource exists.
- **Used by:** Details › Connections & history › **Ethernet ports** › **Open**.
- **Response read:** `ports` (or `interfaces`) list. For each port:
  - `interface_number`
  - `name`
  - `port_status`: values containing `DISCONNECTED` / `NOT_CONNECTED`, or equal to `DOWN` / `NO_LINK`, show "Nothing plugged in". Values containing `DISABLED` show "Turned off". Values containing `CONNECTED`, or equal to `UP`, show "Connected".
  - `negotiated_speed`, `supported_speed`: codes such as `P1000` show as "1 Gbps" and `P2500` as "2.5 Gbps".
  - `network_type`
  - `poe_status` / `poe_state`
  - `ethernet_address`
  - `actions[].type`
- **Also read:** the top-level `node_actions[].type`.

#### POST {eero_url}/ports/{interface_number}/action
- **Used by:** an action button inside the Ethernet ports dialog, one per supported type in the port's `actions[]`.
- **Confirmation:** "<Action> on <port>? Anything plugged into it may lose its connection or power for a moment."
- **Encoding:** JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `action` | string | `ENABLE_PORT`, `DISABLE_PORT`, `ENABLE_DATA`, `DISABLE_DATA`, `ENABLE_POE`, `DISABLE_POE`, `RESTART_POWER`, `ENABLE_PORT_SECURITY`, `DISABLE_PORT_SECURITY` | Only types the port lists are offered. Anything else, such as cable tests, is not offered. |

#### POST {eero_url}/action
- **Used by:** the Ethernet ports dialog › **Restart power on every port** / **Restart power on every port and the eero**. These buttons are shown only when listed in `node_actions`.
- **Confirmation:** "Apply this action to every Ethernet port on this eero? ..."
- **Encoding:** JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `action` | string | `POWER_CYCLE_ALL_PORTS` or `POWER_CYCLE_ALL_PORTS_AND_REBOOT` | |

#### GET /2.2/eeros/{eero_id}/firmware_update/history
- **Used by:** Details › **Software update history** › **View**.
- **Query:** `limit=20&offset=0`.
- **Note:** `{eero_id}` is the last segment of `{eero_url}` and is not checked against the identifier pattern.
- **Response:** shown as read-only details.

#### GET /2.2/networks/{network_id}/data_usage/eeros/{eero_id}
- **Used by:** Details › **Data usage** › **View**.
- **Query:** `start`, `end` (last 7 days), `cadence=daily`.
- **Response:** shown as read-only details.

#### POST /2.2/eeros/{serial}/led_cycle
- **Used by:** Details › Lights › **Find this eero** › **Flash light**. The row appears only when the eero reports a `serial`.
- **Encoding:** form.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `colors[]` | list of string (key repeated) | Each one of `blue`, `green`, `white`, `off`; 1 to 8 colors | The dialog sends two: First color (default `blue`) and Second color (default `white`) |
| `duration` | integer | 1–600 seconds. Default `30`. | |
| `time_per_color` | integer | 1–60 seconds. Default `1`. | |

- `{serial}` must match `^[A-Za-z0-9-]{8,40}$` and is URL-escaped.

#### POST resource `reboot` of the eero (fallback `{eero_url}/reboot`)
- **Used by:** Details › Manage › **Restart**.
- **Confirmation:** "Restart this eero? Connected devices may lose internet access temporarily."
- **Encoding:** none.

#### DELETE {eero_url}
- **Used by:** Details › Manage › **Remove from network**.
- **Confirmation:** you must type the exact eero `location`, then confirm a second dialog that warns about removing the gateway.
- **Encoding:** none.
- **Afterwards:** the app goes back to Home › eeros.

### Home › eeros › Details (more actions)

Page also displays `client_ap_disabled` ("Off on this eero (backhaul only)" / "On") and `state` from the eero object.

#### POST /2.2/eeros/{eero_id}/nightlight/override
"Preview nightlight": shows the nightlight briefly without saving. Shown only when the eero has a `nightlight` object.

- Encoding: **form**.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `brightness_percentage` | int | 0–100 | Default: current `nightlight.brightness_percentage`, else `50` |

#### GET /2.2/eeros/{eero_id}/authorization_status?network_id={network_id}
"Authorization" › Check: whether this eero is allowed on the network. Response shown as-is.

#### GET /2.2/eeros/{serial}/license_expiration?network={network_id}
"License" › Check (provider-supplied eeros). Shown when the eero has a serial. Response shown as-is.

#### GET /2.2/eeros/{serial}/pre_setup_config
"Setup settings from provider" › View. Response shown as-is.

#### POST /2.3/networks/{network_id}/placement_rating/{serial}
"Placement rating" › Rate (also from Setup tools, where the eero is first picked from eeros that have a serial).

- Encoding: **JSON**; all fields optional, so `{}` is valid. Response shown as-is. No refresh.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `rssi` | number | −120 to 0 (dBm), decimal allowed | Omitted when blank |
| `mcs` | int | 0–15 | Omitted when blank |
| `freq` | int | 2400–7200 (MHz) | Omitted when blank |

Example: `{"rssi":-61.5,"mcs":7,"freq":5180}`

### Home › eeros › Placement tests

#### POST /2.3/networks/{network_id}/placement_test/{serial}
#### GET /2.3/networks/{network_id}/placement_test/{serial}
- **Used by:** each eero with a `serial` gets **Run test** (POST, with no body, after "Run a placement test for <location>?") and **View results** (GET; shown as read-only details).
- **Rules:** `{serial}` must match `^[A-Za-z0-9-]{8,40}$` and is URL-escaped.

### Home › Add or replace eero

#### POST /2.2/eeros?requireSession=true
- **Used by:**
  - Home › Add or replace eero › **Add by serial number** › **Add eero**.
  - Home › Add or replace eero › Automatic setup › **Add** on a discovered eero (serial pre-filled).
- **Confirmation:** "Add the eero with this serial number to the selected network?" (for a discovered eero: "Add eero <serial> to the selected network?")
- **Encoding:** form. Example body: `serial=TESTSERIAL123&location=Office&network=%2F2.2%2Fnetworks%2Fnew-7`.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `serial` | string | Trimmed. Must match `^[A-Za-z0-9-]{8,40}$`. | |
| `location` | string | 1–100 characters after trimming (for example Kitchen) | Trimmed |
| `network` | string | The selected `{network_url}`, for example `/2.2/networks/123` | |

#### GET {network_url}/auto_discovered_eeros
- **Used by:** the Automatic setup page load.
- **Response read:** a list (the response itself or `eeros`). For each entry:
  - `serial`
  - `model`
  - `ineligible_for_zts.reason` or `ineligible_for_zts.message`, shown as "Can't join automatically: …".

#### PUT {network_url}/auto_setup_mode
- **Used by:** Automatic setup › **Automatic setup** › **Change**.
- **Confirmation:** "Change automatic setup to <label>?"
- **Encoding:** JSON.
- **Current value:** read from the network's `auto_setup_mode`.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `mode` | string | `"DEFAULT"` (Provider default), `"ZTS"` (ZTS policy), `"LTS"` (LTS policy), `"OFF"` (Off). Any other value is rejected. | The dialog defaults to Provider default when the current value is unknown |

### Home › Add or replace eero › Setup tools

#### GET /2.3/networks/{network_id}/pending_eeros
"Waiting to be set up" › View. Response shown as-is.

#### GET /2.2/networks/{network_id}/packaged_eeros
"Came in the box" › View. Network-relative. Response shown as-is.

#### GET /2.2/eero_models/capabilities?networkId={network_id}
"What each model supports" › View. Response shown as-is.

#### GET /2.2/networks/{network_id}/ouicheck?serial={serial}[&version={version}]
"Check compatibility". Network-relative.

| Query | Type | Values / rules | Notes |
|---|---|---|---|
| `serial` | string | `^[A-Za-z0-9-]{8,40}$` | Required |
| `version` | string | `^[A-Za-z0-9_.:-]{1,64}$` | Optional software version, e.g. `v7.1.1` |

#### POST /2.2/networks/{network_id}/node_setup
"Use the default wifi channel": temporarily moves the network to its default channel so a new eero can find it. Network-relative.

- Encoding: **form**. Confirmation: "Move this network to its default wifi channel for N minutes? Devices may briefly reconnect."

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `default_channel_duration` | int | Seconds, 60–7200 | User enters minutes 1–120 (default 60); sent as minutes × 60 (default `3600`) |

#### POST /2.2/eeros/networks/{network_id}/notify_swap
"Report a replacement".

- Encoding: **JSON**.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `old_eero_serial` | string | Serial rule | |
| `new_eero_serial` | string | Serial rule; must differ from old (case-insensitive) | |
| `succeeded` | bool | Default `true` | "The replacement worked" |

### Home › Add or replace eero › Create a network

Flow: enter serial → fetch agreements → fill in network details and accept agreements → create network → claim the gateway into it. Not available for PPPoE/static-IP internet, cellular, or gateway replacement.

#### GET /2.2/consents/network?serial={serial}
Step 1. `serial` must match the serial rule and must not belong to an eero already on the active network.

Response: array of agreements, each with `name` (required), `localized_consent` (required; missing either aborts setup), `requires_consent` (bool), `links[].link` (each must be an `https://eero.com` or `https://*.eero.com` URL on port 443, else setup aborts). Each agreement becomes a checkbox labelled "(required)" or "(optional)".

#### POST /2.3/networks
Step 2 "Create network". Encoding: **JSON**. Confirmation: "Create a new network named <name> and add the gateway with serial <serial>? Your current network is separate." Only one creation request per setup: if the connection drops the user is told to check the account instead of retrying.

```json
{
  "name": "MyNewWifi",
  "password": "correct-horse",
  "timezone": { "value": "America/Chicago" },
  "settings": { "consents": { "<agreement name>": true }, "enabled_simple_setup_vendors": [] }
}
```

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `name` | string | Trimmed; 1–32 UTF-8 bytes | New wifi name |
| `password` | string | 8–63 printable ASCII | |
| `timezone.value` | string | Trimmed; `^[A-Za-z_+-]+(?:/[A-Za-z_0-9+-]+){0,3}$` | Prefilled from the active network's timezone |
| `settings.consents` | object | One key per agreement `name` → bool accepted; every `requires_consent: true` agreement must be `true` | |
| `settings.enabled_simple_setup_vendors` | int[] | Always `[]` | |

(The "Gateway location" field is validated here — trimmed, 1–100 characters — but sent in the next call.)

Response read: `data.url`; must resolve to a path matching `^/2\.[23]/networks/[A-Za-z0-9_-]+$` with no query string, otherwise setup stops with "The server did not return a valid created network."

#### POST /2.2/eeros?requireSession=true (Create a network)
Step 3: claim the gateway into the new network (runs automatically after creation; a "Continue" button retries it without creating another network). Encoding: **form**.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `serial` | string | Serial rule | From step 1 |
| `location` | string | Trimmed; 1–100 characters | Gateway location |
| `network` | string | Path of the created network, e.g. `/2.3/networks/123` | |

After success the client reloads the account, selects the new network (`GET` its url) and returns to Home.

## 6. Devices

### Devices › Device details and actions

- **`{device_url}`:** the device's `url`, or `{network_url}/devices/{device_mac}` when the device has none.
- **`{device_mac}`:** the device's `mac`, URL-escaped.

#### PUT {device_url}: pause / resume
- **Used by:** the device panel › **Internet access** switch.
- **Confirmation:** "Resume/Pause internet for <name>?"
- **Encoding:** JSON. Uses the lists-only refresh.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `paused` | boolean | `true` = pause, `false` = resume | Example: `{"paused":true}` |

#### PUT {device_url}: nickname
- **Used by:** the device panel › **Nickname** › **Change**.
- **Encoding:** JSON. Uses the lists-only refresh.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `nickname` | string | 1–100 characters after trimming | Trimmed |

#### PUT {device_url}: profile
- **Used by:** the device panel › **Profile** › **Change**. The row appears only when at least one profile has a `url`.
- **Encoding:** JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `profile` | object | `{ "url": "<profile url>" }`, chosen from the network's profiles | There is no "None" option, so you cannot remove a device from its profile here |

#### PUT {network_url}/devices/{device_mac}: device type
- **Used by:** the device panel › **Device type** › **Change**.
- **Encoding:** JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `device_type` | string | One of: `air_conditioner`, `air_purifier`, `alarm_system`, `audio`, `bed`, `cable_box`, `car`, `cash_register`, `coffee_maker`, `credit_card_terminal`, `desktop_computer`, `digital_assistant`, `door_bell`, `door_lock`, `exercise_bike`, `e_reader`, `fan`, `game_console`, `garage_door`, `generic`, `hard_drive`, `hub`, `laptop_computer`, `light`, `media_streamer`, `network_equipment`, `oven`, `pet_device`, `phone`, `plug`, `printer`, `refrigerator`, `remote`, `scale`, `security_camera`, `smoke_detector`, `sprinkler`, `tablet`, `television`, `thermostat`, `toaster`, `unknown_computer`, `vacuum`, `washer_dryer`, `watch` | The dialog defaults to `generic` when the current type is unknown |

#### PUT /2.3/networks/{network_id}/devices/{device_mac}: internet backup access
- **Used by:** the device panel › **Use internet backup** switch. The switch appears only when the device's `secondary_wan_deny_access` is a boolean.
- **Encoding:** JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `secondary_wan_deny_access` | boolean | `false` = allowed (switch on), `true` = denied | This is the inverse of the switch |

#### GET {network_url}/data_usage/devices/{device_mac}
- **Used by:** the device panel › **Data usage** › **View**.
- **Query:** `start`, `end` (last 7 days), `cadence=daily`.
- **Response:** shown as read-only details.

#### POST {network_url}/blacklist: block
#### DELETE {network_url}/blacklist/{device_mac}: unblock
- **Used by:**
  - The device panel › **Block device** / **Unblock**. The button shown depends on the device's `blocked` flag.
  - Devices › Blocked devices › **Unblock** (DELETE).
- **Confirmation:**
  - "Block <name> on this network? If it's this PC, the app may lose its connection."
  - "Allow <name> on this network?"
  - On the Blocked devices page: "Allow <name> to join your network again?"
- **Encoding:** POST uses form. DELETE has no body.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `mac` | string | The device's `mac` as reported | POST only |

#### GET {network_url}/blacklist
- **Used by:** the Devices › Blocked devices page load.
- **Response read:** a list (the response itself or `devices`). For each entry: `mac`, `manufacturer` and the name fields.

### Devices › Blocked devices

#### GET /2.2/networks/{network_id}/blacklist
Network-relative. Response: array or `{ devices: [...] }`. Item fields read: `mac`, `manufacturer`, plus name fields used for display (nickname/hostname etc.).

#### DELETE /2.2/networks/{network_id}/blacklist/{device_mac}
Unblock. `{device_mac}` is the `mac` exactly as returned, URL-escaped (`:` → `%3A`). Body **none**. Confirmation: "Allow <device> to join your network again?"

## 7. Activity

### Activity › Internet (speed)

#### GET /2.2/networks/{network_id}/speedtest
**(resource-overridable: `speedtest`)** Page data for **Home › Internet**.

- Query: none. Body: none.
- Response: the client reads a list from `data` (either `data` itself is the list or it holds `speed_tests` / `data`), and shows the first 10 entries:

| Field | Type | Notes |
|---|---|---|
| `date` / `created` / `created_at` / `timestamp` | string | First present one is the row title |
| `down_mbps` / `down` | number | Rounded and shown as "↓ N Mbps". If not numeric, `down.value` is shown |
| `up_mbps` / `up` | number | Same as above for upload, falls back to `up.value` |

The page also reads, from the network object: `isp` / `isp_name`, `health.internet.status`, `gateway_ip`, `speed.down.value`, `speed.down.units` (default "Mbps"), `speed.up.value`, `speed.up.units`.

#### POST /2.2/networks/{network_id}/speedtest
**(resource-overridable: `speedtest`)** Button **Home › Internet › Speed test › Run test**.

- Body: none. No confirmation. The response is not read. No automatic refresh (the user is told to refresh later).

### Activity › Data usage

Time window format used below: `start` and `end` are UTC timestamps formatted `yyyy-MM-ddTHH:mm:ssZ` and URL-escaped (so `:` becomes `%3A`). `end` = now, `start` = now minus N days.

#### GET /2.2/networks/{network_id}/data_usage
Page data for **Activity › Data usage**; also re-read when the **Period › Change** dialog is saved.

| Query parameter | Type | Values / rules | Notes |
|---|---|---|---|
| `start` | timestamp | see format above | now − N days |
| `end` | timestamp | see format above | now |
| `cadence` | string | `daily` | Always `daily` |

N (the period) is chosen from `1 day`, `7 days`, `30 days`; default `7 days`. The response is drawn as usage charts (series data).

#### GET /2.2/networks/{network_id}/data_usage/breakdown
Button **Activity › Data usage › Who used the most › View**.

- Query: `start`, `end` (same window as the current period). No `cadence`.
- Response is displayed as-is (usage per device and profile).

#### GET /2.2/networks/{network_id}/data_usage/report_settings
Read when **Usage reports › Change** is opened. Response field read: `cadence` (`"monthly"` preselects Monthly, anything else preselects Weekly).

#### PUT /2.2/networks/{network_id}/data_usage/report_settings
Saving **Activity › Data usage › Usage reports**. Body: JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `cadence` | string | `weekly` \| `monthly` | From the choice "Weekly" / "Monthly" |
| `notification_day` | integer | `1` when weekly, `31` when monthly | Fixed by the client; not user-editable |

Example: `{"cadence":"weekly","notification_day":1}`

### Activity › Network events

#### GET /2.2/networks/{network_id}/app_events
Page data for **Activity › Network events**.

- Query: `page_size=50`.
- Response: a list (from `data`, or `data.data`, `data.events`, `data.app_events`). The first 50 are shown, newest first as returned.

| Field | Type | Notes |
|---|---|---|
| `message` / `title` / `description` / `summary` | string | Row title (first present) |
| `event_type` / `type` / `category` | string | Shown as the event kind |
| `created` / `timestamp` / `occurred_at` / `time` | string | First non-empty one is shown as the time |

### Activity › Security activity

Requires an eero plan with Advanced Security (an error response is shown as "not available" / "can't open").

#### GET /2.2/networks/{network_id}/insights
Page data for **Activity › Security activity**. Query: `start`, `end` (last 7 days), `cadence=daily`. Drawn as charts.

#### GET /2.2/networks/{network_id}/insights/devices
Button **Security activity › By device › View**. Query: `start`, `end` (last 7 days). No `cadence`. Displayed as-is.

#### GET /2.2/networks/{network_id}/insights/profiles
Button **Security activity › By profile › View**. Query: `start`, `end` (last 7 days). No `cadence`. Displayed as-is.

### Activity › Usage details

All network-relative under `/2.2/networks/{network_id}/` (built with a fixed `/2.2/`). Period selector: 1, 7 (default), 30 or 90 days. Responses are shown as-is.

#### GET /2.2/networks/{network_id}/data_usage/eeros/summary?start={start}&end={end}&cadence=daily
Data usage › Each eero › All eeros.

#### GET /2.2/networks/{network_id}/data_usage/eeros/{eero_id}?start={start}&end={end}&cadence=daily
Data usage › Each eero › Pick one (also eero Details › Data usage, fixed 7-day range).

#### GET /2.2/networks/{network_id}/data_usage/profiles/{profile_id}?start={start}&end={end}&cadence=daily
Data usage › A profile.

#### GET /2.2/networks/{network_id}/data_usage/unprofiled/devices?start={start}&end={end}
Devices not in a profile › Each device (no `cadence`).

#### GET /2.2/networks/{network_id}/data_usage/unprofiled/summary?start={start}&end={end}&cadence=daily
Devices not in a profile › Total.

#### GET /2.2/networks/{network_id}/insights/devices/{device_mac}?start={start}&end={end}&cadence=daily
Blocked threats › A device. `{device_mac}` normalised lowercase colon form, URL-escaped.

#### GET /2.2/networks/{network_id}/insights/profiles/{profile_id}?start={start}&end={end}&cadence=daily
Blocked threats › A profile › Total.

#### GET /2.2/networks/{network_id}/insights/profiles/{profile_id}/devices?start={start}&end={end}
Blocked threats › A profile › Per device (no `cadence`).

## 8. Settings

### Settings › Wifi name & password

#### PUT {network_url}
Change the main wifi name (SSID).

- Encoding: **form**
- Confirmation: "Change the wifi name? Devices may need to reconnect."

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `name` | string | Trimmed; 1–32 UTF-8 bytes; not blank | Current value prefilled from `network.name` |

#### GET /2.2/networks/{network_id}/password
Reveal the main wifi password (Show, Show QR code, Copy wifi details; also the home-screen "Share QR code" button). Resource-aware: `network.resources.password`.

- Response: `data` is either a plain string (the password) or an object with `password`. Empty → error "eero did not return the wifi password for this account."
- The QR code is built locally as `WIFI:T:WPA;S:<name>;P:<password>;;` (`T:nopass` when empty; `\ ; , : "` escaped with backslash).

#### PUT /2.2/networks/{network_id}/password
Change the main wifi password. Resource-aware (`resources.password`).

- Encoding: **form**
- Confirmation: "Change the wifi password? Devices will need the new password to reconnect."

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `password` | string | 8–63 characters, printable ASCII only (code points 32–126) | Not trimmed |

#### POST /2.2/util/generatepassword
"Generate a strong password" — asks the server for a suggestion; nothing is changed.

- Encoding: **JSON**, body `{}`
- Response: `data` as a string, otherwise `data.password` or `data.value`. Empty → error. The value is shown with a Copy button; the user must apply it with the password change above. No refresh.

### Settings › Guest wifi network

#### GET /2.2/networks/{network_id}/guestnetwork
Loads the page. Resource-aware (`resources.guestnetwork`).

Response fields read: `url` (used as the guest URL for writes if present), `name`, `enabled`, `resources.password` (guest password URL; default `<guest url>/password`).

#### PUT /2.2/networks/{network_id}/guestnetwork
Turn guest wifi on/off, or rename it. Uses `data.url` if returned, else the resource-aware path.

- Encoding: **form**
- Confirmation only when turning off: "Turn off guest wifi? Devices on the guest network will disconnect."

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `name` | string | Rename: trimmed, 1–32 UTF-8 bytes | Toggle sends the current name unchanged |
| `enabled` | bool | `true` / `false` | Rename sends the current state unchanged |

#### GET /2.2/networks/{network_id}/guestnetwork/password
Reveal guest password / guest QR code. URL = `guest.resources.password` or `<guest url>/password`. Response: string or `{ password }`.

#### PUT /2.2/networks/{network_id}/guestnetwork/password
- Encoding: **form**; no confirmation.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `password` | string | 8–63 printable ASCII characters | |

### Settings › Nickname & location

#### PUT /2.2/networks/{network_id}/label?labelType=specialMarket
Network nickname (a label in eero apps; not the SSID). Network-relative.

- Encoding: **form**; query `labelType=specialMarket` is fixed.
- Current value read from `network.nickname_label`, else `network.nickname`.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `label` | string | Trimmed; 1–100 characters; not blank | |

(Timezone is written through `PUT …/settings`, below.)

### Settings › Network settings (shared write)

#### PUT /2.2/networks/{network_id}/settings
Single endpoint for many network-wide settings. Resource-aware (`resources.settings`). Encoding: **JSON** for every variant. Each variant sends only the keys shown.

##### Variant: timezone (Settings › Nickname & location › Timezone)
Confirmation: "Change the network timezone? Scheduled pauses will follow the new timezone."
```json
{ "timezone": { "value": "America/Chicago" } }
```
| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `timezone.value` | string | Trimmed; must match `^[A-Za-z_+-]+(?:/[A-Za-z0-9_+-]+){1,2}$` or be exactly `UTC` | IANA region/city. Current value from `network.timezone.value` or `network.timezone` |

##### Variant: internet address / WAN lease (Settings › Advanced networking › Internet connection › Address assignment)
Confirmation: "Apply the internet addressing settings supplied by your ISP? Incorrect values can disconnect the network."
```json
{ "lease": { "mode": "dhcp" } }
{ "lease": { "mode": "static", "static": { "ip": "203.0.113.10", "mask": "255.255.255.0", "router": "203.0.113.1" } } }
```
| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `lease.mode` | string | `dhcp` ("Automatic (DHCP)") or `static` ("Static IP") | |
| `lease.static.ip` | string | IPv4 dotted quad (`^(\d{1,3}\.){3}\d{1,3}$` and parseable) | Only when `static` |
| `lease.static.mask` | string | IPv4 dotted quad | Only when `static` |
| `lease.static.router` | string | IPv4 dotted quad | Gateway; only when `static` |

Page reads `network.lease.mode`, `network.lease.static.{ip,mask,router}`, `network.connection.mode`, and shows `connection`, `ip_settings`, `lease`, `ipv6_lease` as technical details.

##### Variant: DNS (Settings › Advanced networking › DNS)
Server change confirmation: "Apply DNS settings? eero may restart the network." Caching toggle confirmation: "Turn on/off local DNS caching? eero may restart the network."
```json
{ "dns": { "mode": "automatic", "caching": true } }
{ "dns": { "mode": "custom", "caching": false, "custom": { "ips": ["1.1.1.1", "2606:4700:4700::1111"] } } }
```
| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `dns.mode` | string | `automatic` or `custom` | |
| `dns.caching` | bool | | Server change keeps the current `network.dns.caching`; caching toggle keeps current mode and servers |
| `dns.custom.ips` | string[] | Input split on `,` `;` newline, space; each entry trimmed and must parse as IPv4 or IPv6; 1–4 entries when custom; more than 4 rejected | Present only when `mode = custom`. Sent as typed (not re-formatted) |

Page reads `network.dns.mode`, `network.dns.caching`, `network.dns.custom.ips`.

##### Variant: network mode / DHCP & NAT (Settings › Advanced networking › DHCP & NAT)
Modes offered: Automatic, Manual, Bridge, and IP Passthrough (only if `network.capabilities.ip_passthrough.capable` is true or the network is already in passthrough).
Confirmation for passthrough: "Switch to IP Passthrough? The network will restart, your public IP passes to one downstream device, and routing, profiles and some eero Plus features stop working." Otherwise: "Apply DHCP & NAT settings? The network may restart. Bridge mode disables routing, profiles and some security features."

```json
{ "connection": { "mode": "nat" }, "nat_port_randomization": true, "dhcp": { "mode": "automatic" } }
{ "connection": { "mode": "nat" }, "nat_port_randomization": false,
  "dhcp": { "mode": "custom", "custom": { "subnet_ip": "192.168.4.0", "subnet_mask": "255.255.255.0", "start_ip": "192.168.4.10", "end_ip": "192.168.4.200" } } }
{ "connection": { "mode": "bridged" }, "nat_port_randomization": false }
{ "connection": { "mode": "ipPassthrough" }, "nat_port_randomization": false }
```
| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `connection.mode` | string | `nat` (Automatic, Manual), `bridged` (Bridge), `ipPassthrough` (IP Passthrough) | |
| `nat_port_randomization` | bool | | Always sent. The switch is only visible for Automatic/Manual; for Bridge/Passthrough the value is whatever the (hidden) switch holds, initially the current `network.nat_port_randomization` (it may therefore be `true` or `false`) |
| `dhcp` | object | Omitted for `bridged` and `ipPassthrough` | |
| `dhcp.mode` | string | `automatic` or `custom` (Manual) | |
| `dhcp.custom.subnet_ip` | string | IPv4; must equal `subnet_ip & subnet_mask` | Manual only |
| `dhcp.custom.subnet_mask` | string | IPv4; non-zero, contiguous mask | Manual only |
| `dhcp.custom.start_ip` | string | IPv4 inside the subnet; > network address; ≤ `end_ip` | Manual only |
| `dhcp.custom.end_ip` | string | IPv4 inside the subnet; < broadcast address | Manual only |

Page reads `network.connection.mode` (`bridged` / `ipPassthrough`), `network.dhcp.mode`, `network.dhcp.custom.*`, `network.nat_port_randomization`, `network.capabilities.ip_passthrough.capable`.

##### Variant: NAT port randomization toggle (DHCP & NAT page)
Shown only when `network.nat_port_randomization` is a boolean and mode is Automatic or Manual. Confirmation: "Turn on/off NAT port randomization? The network may restart."
```json
{ "nat_port_randomization": true }
```

##### Variant: single boolean toggles
Each switch is shown only when the corresponding `network.<key>` is a boolean. Body is `{ "<key>": true|false }`. Confirmation: "Turn on/off <title>? <suffix>".

| Key | Page / switch | Confirmation suffix |
|---|---|---|
| `wpa3` | Wireless settings › WPA3 security | "Devices may briefly reconnect." |
| `band_steering` | Wireless settings › Client steering | same |
| `sqm` | Wireless settings › Optimize for conferencing and gaming | same |
| `ffs` | Wireless settings › Amazon Frustration-Free Setup | same |
| `ipv6_upstream` | Advanced networking › IPv6 | "Some devices may reconnect." |
| `upnp` | Advanced networking › UPnP | "Some devices may reconnect." |

### Settings › User permissions

#### GET /2.2/networks/{network_id}/members
Page data (part 1) for **Settings › User permissions**. Response: list (from `data` or `data.members`).

| Field | Type | Notes |
|---|---|---|
| `user_id` / `id` | string/number | Member identifier used in actions |
| `user_role` | string | `owner`, `admin`, or another role. Shown as Owner / Administrator / friendly role / Member |
| `user_name` / `name` / `user_email` / `email` / `phone` | string | Display name (first present) |

#### GET /2.2/networks/{network_id}/invites
Page data (part 2). Response: list (from `data` or `data.invites`). Entries without an id are skipped.

| Field | Type | Notes |
|---|---|---|
| `invite_id` / `id` | string/number | Invitation identifier |
| `invite_nickname` | string | Shown as the row title (default "Administrator invitation") |
| `created` / `created_at` | string | Shown as "Created …" |

#### POST /2.2/networks/{network_id}/member_promotion
Button **Make admin** on a member row. Shown only when the member has an id and `user_role` is neither `owner` nor `admin`. Confirmation: "Invite <name> to become an administrator?". Body: JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `member_id` | string | validated identifier | The member's `user_id` / `id` |

#### DELETE /2.2/networks/{network_id}/admins/{user_id}
Button **Remove** on a member row. Shown when the member has an id and is not `owner`. Confirmation: "Remove administrator access for <name>?". Body: none. `{user_id}` is URL-escaped (not otherwise validated).

#### POST /2.2/networks/{network_id}/invites
Button **Invite an administrator › Create link**. Confirmation required. Body: JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `invite_role` | string | `admin` | Fixed |

Example: `{"invite_role":"admin"}`

Response: `invite_url` (string). If missing, an error is shown. The link is shown in a dialog with a copy button; then the page data is re-read. (This call does not trigger the network re-read.)

#### PUT /2.2/networks/{network_id}/invites/{invite_id}
Button **Rename** on a pending invitation. Body: JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `invite_nickname` | string | Name rule; sent trimmed | Only visible to the sender |

#### DELETE /2.2/networks/{network_id}/invites/{invite_id}
Button **Cancel** on a pending invitation. Confirmation required. Body: none.

#### POST /2.2/networks/{network_id}/invites/cancel_pending_admin
Button **Cancel all invitations** (shown only when more than one invitation is pending). Confirmation required. Body: none.

### Settings › User permissions › Join a network

#### POST /2.2/inviteQuery
Button **Review invitation**. Body: JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `invite_code` | string | `^[A-Za-z0-9_~.+/=-]{4,2048}$` after extraction | Input box max length 1024 |

Code extraction: if the input is an absolute URL it must be `https`, port 443, no user-info, host `eero.com` or a subdomain of `eero.com`. The code is taken from the first query parameter named `invite_code`, `inviteCode` or `code`; if none, from the last path segment. Otherwise the trimmed input is used as the code. The link itself is never opened.

Response fields read: `network_id` (validated identifier), `invite_status` (Accept/Decline are enabled only when it equals `pending`, case-insensitive), `network_name` (used in the confirmation). All fields are displayed.

#### POST /2.2/networks/{network_id}/invites/response
Buttons **Accept invitation** / **Decline invitation** in the review dialog. `{network_id}` is the `network_id` from the query response. Confirmation: "Accept/Decline the invitation to <network_name>?". Body: JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `invite_code` | string | same rules as above | |
| `accept` | boolean | `true` (accept) \| `false` (decline) | |

Example: `{"invite_code":"sample_code-123","accept":false}`

After success the client clears all network state and reloads the account (`GET /2.2/account` and the network list), then returns to Home.

### Settings › User permissions › Incoming access invitations

#### GET /2.2/incoming_member_promotions
- **Used by:** the page load.
- **Response read:** a list (the response itself or `promotions`). For each entry:
  - `invite_id`
  - `network_id`
  - `network_name`
  - `invite_role`: `admin` shows as "Administrator: can change every setting".

  **Accept** and **Decline** appear only when both `invite_id` and `network_id` are present.

#### POST /2.2/networks/{network_id}/invites/response (Incoming access invitations)
- **Used by:** **Accept** or **Decline** on an invitation.
- **Confirmation:** "Accept/Decline this access invitation for <network>?"
- **Encoding:** JSON.
- **Path:** `{network_id}` is the invitation's `network_id` and must match `^[A-Za-z0-9_-]{1,128}$`.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `invite_id` | string | Must match `^[A-Za-z0-9_-]{1,128}$` | |
| `accept` | boolean | `true` = accept, `false` = decline | Example: `{"invite_id":"invite-7","accept":false}` |

### Settings › Software updates

#### GET /2.2/networks/{network_id}/updates
**(resource-overridable: `updates`)** Page data.

| Field | Type | Notes |
|---|---|---|
| `update_status` | string | Lower-cased. Contains `available`, or equals `pending` / `downloading` → update exists (used only when no boolean flag is present). `up_to_date` / `current` → up to date |
| `has_update` | boolean | Preferred indicator |
| `update_available` | boolean | Used if `has_update` is absent |
| `update_to_firmware` / `target_firmware` | string | Target version. If every eero's `os_version` / `firmware_version` (leading `v` stripped, lower-cased) equals it, the state is "Up to date" regardless of flags |
| `can_update_now` | boolean | Enables **Install now** (only when an update is available) |
| `preferred_update_hour` | integer 0–23 | Current automatic update hour; "Not set" if missing or out of range |

#### POST /2.2/networks/{network_id}/updates
**(resource-overridable: `updates`)** Button **Install update › Install now**. Enabled only when an update is available and `can_update_now` is `true`. Confirmation: network restarts during installation.

- Body: JSON, the literal empty JSON string `""` (Content-Type application/json).

#### POST /2.2/networks/{network_id}/updates/preferred_update_hour
**(path = updates resource + `/preferred_update_hour`)** Button **Automatic update time › Change**. Body: JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `preferred_update_hour` | integer | `0`–`23` (index into a 24-hour choice list) | Pre-selected: current value, or 3 (3 AM) when not set |

### Settings › Notifications

#### GET /2.2/networks/{network_id}/notifications
Page data. The response object's boolean-valued keys become one switch each (non-boolean keys are ignored). Known keys and labels: `device_new` / `new_device` (A new device joins), `network_updated`, `network_offline`, `network_online`, `eero_offline` / `node_offline`, `speed_test`, `security`, `data_usage`. Dots in keys are treated as underscores for labeling only.

#### PUT /2.2/networks/{network_id}/notifications
Toggling any **Network alerts** switch. Body: JSON — the **whole set** of boolean keys exactly as received (original key names), with the toggled key replaced by the new value.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `<each key returned by GET>` | boolean | `true` / `false` | All keys are sent together |

#### PUT /2.2/account/push_settings
Toggling any **Account alerts** switch. Source data is the account object's `push_settings` (from `GET /2.2/account`). Body: JSON — all boolean keys of `push_settings`, with the toggled key replaced. If `push_settings` has no boolean keys, the group is hidden.

#### GET /2.2/networks/{network_id}/notifications_history
Button **History › Recent notifications › View**. Query: `limit=50`. Displayed as-is.

#### GET /2.2/networks/{network_id}/notifications/has_unread
Button **Unread notifications › Check**. Always built as `/2.2/networks/{network_id}/...` (validated identifier). Response: either a bare boolean `data`, or `data.has_unread` (boolean).

#### POST /2.2/networks/{network_id}/notifications/mark_read
Button **Unread notifications › Mark all read**. Body: none.

### Settings › Notifications › Windows notifications

#### GET {network_url}/notifications_history
- **Used by:**
  - The Windows tray alerts, while "notifications" is turned on in local preferences.
  - The request runs once a minute, for every network in the account (the default) or only the selected network.
  - It is skipped while the app is busy or signed out.
- **Query:** `limit=50`.
- **Response read:** a list (the response itself, `notifications` or `items`). For each entry:
  - `uuid` (or `id`): the key for "already seen".
  - `title` and `body`.
- **Rules:**
  - The first check for an account and network only records IDs, without alerting.
  - Later checks raise one balloon for new IDs. The title is the single item's `title`, or "<n> eero network updates". The text is "<network name>: <body>".
  - Seen IDs are stored encrypted on the PC, kept per account and network, and capped at 256 each.
  - A network `url` outside the trusted API host is rejected.
  - Session expiry signs you out and shows "Sign in again to restore network alerts."

### Settings › Troubleshooting

#### POST /2.2/networks/{network_id}/diagnostics
**(resource-overridable: `diagnostics`)** Button **Network diagnostics › Run**. Body: none. Response not read; no refresh.

#### GET /2.2/networks/{network_id}/diagnostics
**(resource-overridable: `diagnostics`)** Button **Network diagnostics › View results**; also page data for **Troubleshooting › Guided diagnostics**. Displayed as-is.

#### GET /2.2/networks/{network_id}/network_scan
Button **Network scan › View**. Displayed as-is.

#### GET /2.4/networks/{network_id}/placement/wifi_coverage_scan
Button **Coverage scan › View**. Query: `allDevices=true`. Version 2.4 is fixed. Displayed as-is.

#### GET /2.4/networks/{network_id}/placement/wifi_generation_scan
Button **eero upgrade report › View**. Query: `allDevices=true`. Displayed as-is.

### Settings › Troubleshooting › Guided diagnostics

#### POST resource `diagnostics` (fallback `{network_url}/diagnostics`)
- **Used by:** the **Check** button next to each symptom.
- **Encoding:** JSON. This call does not trigger a refresh.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `symptom` | string | `"internet_slow"` (My internet is slow), `"internet_offline"` (My internet is offline), `"connection_drops"` (My connection keeps dropping), `"eeros_red"` (My eero lights are red). Any other value is rejected. | No device field is sent |

#### GET resource `diagnostics` (fallback `{network_url}/diagnostics`)
- **Used by:** the page load and **Show results**.
- **Response read:**
  - `status`: `completed` or `complete` means finished.
  - `issue_area` (or `issue`): empty or `none` shows "No problems found". Any other value shows "Problem found: <area>".
  - The rest of the response appears under technical details.

### Settings › Troubleshooting › Connect a device

#### PUT {network_url}/temporary_flags/hide_5g
#### DELETE {network_url}/temporary_flags/hide_5g
- **Used by:** the **Hide 5 GHz for device setup** switch.
  - Turning it on sends PUT, after "Hide 5 GHz on this network for now? ..." is confirmed.
  - Turning it off sends DELETE, with no confirmation.
- **Encoding:** PUT uses form. DELETE has no body.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `value` | boolean | always `true` | PUT only |

- **State read:** the network's `temporary_flags.hide_5g`. It is either a boolean, or an object with `value` and an expiry in `expires_at`, `expiration` or `expires`.

### Settings › Account settings › Subscriptions

#### GET /2.2/premium/customer
**Subscriptions › Current plan › Details**; also page data for **Subscriptions › Billing & status**. Displayed as-is. The plan summary itself comes from `premium_status` (string, or object with `status` / `state`) and `premium_details` (`tier` / `plan` / `name`) on the network object, falling back to the account object.

#### POST /2.2/license_keys (validate)
**Subscriptions › Redeem a license key › Enter key › Check key**. Requires a selected network. Body: JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `software_key` | string | Trimmed; length 4–512; no control characters | Entered in a password-style field |

No `network_id` is sent, so this only validates the key. The whole response is shown in a review dialog. (No automatic refresh.)

#### POST /2.2/license_keys (activate)
Button **Activate for this network** in the review dialog. Confirmation states the network name and whether auto-renew is on. Body: JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `software_key` | string | same as above | |
| `network_id` | string | validated identifier | Selected network |
| `auto_renew` | boolean | default `false` | Checkbox "Enable automatic renewal…", unchecked by default |

#### GET /2.2/account/1password_status
**Partner benefits › 1Password › Status**. Displayed as-is.

#### GET /2.2/partners/onepassword/coupon
**Partner benefits › 1Password › Get code**. Query: `test=false`. Response: `coupon` (string). If empty, an error "No redemption code…" is shown; otherwise shown with a copy button.

#### GET /2.2/partners/malwarebytes/coupon
**Partner benefits › Malwarebytes › Get code**. Query: `test=false`. Response: `coupon` (same handling).

### Settings › Account settings › Subscriptions › Offers & referrals

#### GET /2.4/premium/plans/{network_id}
- **Used by:** the page load. It is skipped when no network is selected.
- **Response read:** a list (the response itself or `plans`). For each plan:
  - `id` (plans without one are skipped)
  - `name`
  - `price` / `display_price` / `amount` (first non-empty; otherwise "Price shown at checkout")
  - `interval`

#### POST /2.2/premium/in_app_payments/offers/{network_id}
- **Used by:** Plan row › **Offers**.
- **Encoding:** JSON. This call does not trigger a refresh.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `product_ids` | array of string | 1–50 unique IDs. Each is non-blank, at most 200 characters, with no control characters. | The app sends `[ "<plan id>" ]` |

- **Response read:** a list (the response itself or `offers`). The app shows each entry's `title` and `ends_at`.

#### PUT /2.2/subscription_referrals
- **Used by:** Plan row › **Share link**.
- **Encoding:** JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `product_id` | string | The plan `id`; non-blank, at most 200 characters, no control characters | |
| `network_id` | string | Must match `^[A-Za-z0-9_-]{1,128}$` | Example: `{"product_id":"product-2","network_id":"network-1"}` |

- **Response read:** `referral_url`. It must point to an official eero domain. The dialog shows it with **Copy link** and **Open referral**.

#### POST /2.2/subscription_referral/email
- **Used by:** Plan row › **Email me**.
- **Confirmation:** "Ask eero to email this plan's referral link to your account email address?"
- **Encoding:** JSON. The body is the same as `PUT /2.2/subscription_referrals` (`product_id`, `network_id`).

#### GET {network_url}/managed/subscription_referrals
- **Used by:** Referrals › **Referral status** › **View**. Shown as read-only details.

#### GET /2.2/login/temporary
- **Used by:**
  - Offers & referrals › **Purchases** (`target_page=MANAGE_PURCHASES`).
  - Manage subscriptions & purchases › **Subscription** (`MANAGE_SUBSCRIPTION`), **Purchases** (`MANAGE_PURCHASES`) and **Add-ons** (`MOBILE_ADDON_PAYMENT`).
- **Query:** `client_type=WEB_PAYMENTS&target_page=<MANAGE_SUBSCRIPTION | MANAGE_PURCHASES | MOBILE_ADDON_PAYMENT>`.
- **Response read:**
  - `target_url`: must be an official eero domain.
  - `temporary_token`: sent to that page as the header `x-temp-user-token: <token>`, on the first request only. The header is removed on redirects and other resources, and a token containing CR/LF is rejected.
- **Google Play:** the Google Play link opens `https://play.google.com/store/account/subscriptions` and sends no API request.

### Settings › Account settings › Subscriptions › Entitlements & trials

#### GET /2.2/entitlements/networks/{network_id}/features
Included features. Response: array (or `{ features: [...] }`). Items are strings, or objects with `name`/`feature`/`key`/`slug` and optional `enabled` (missing = included).

#### GET /2.2/entitlements/networks/{network_id}/upsell_features
Upgrades. Response: array (or `{ upsell_features: [...] }` / `{ features: [...] }`). Items: strings or objects with a name field and `tier`/`plan`/`plan_tier` (prefills the trial tier).

#### POST /2.2/premium/customer/plans
"Start trial". Encoding: **form**. Confirmation: "Start a free trial of this plan? Read the terms first. A trial can turn into a paid subscription."

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `autotrial_tier` | string | `^[A-Za-z0-9_.:-]{1,40}$` | e.g. `plus` |

### Settings › Account settings › Subscriptions › Partner offers

Three-step flow: request a sign-in code, verify it, order.

#### GET /2.2/partners/upsells/offers[?opt_network_id={network_id}]
Loaded with the page. Response read: `session_id` / `upsell_session_id`; offers from an array, `offers`, or `data`; each offer's `internal_offer_id` / `offer_id` / `id` (offers without one are skipped), `title` / `name` / `product_name`, `description` / `partner_name` / `partner`.

#### GET /2.2/partners/upsells/authentication_methods
Loaded with the page. Response read: methods from an array, `methods` or `authentication_methods` — each a string or an object with `authentication_method` / `method` / `type`; `session_id` (fallback session).

If no session ID is returned, the user must type one.

#### POST /2.2/partners/upsells/authentication_methods
Step 1 "Send code". Encoding: **JSON**.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `authentication_method` | string | `^[A-Za-z0-9_.:-]{1,64}$`; one of the returned methods, or typed (e.g. `EMAIL`) | Raw server value is sent |
| `authentication_session_id` | string | `^[A-Za-z0-9_.:-]{1,256}$` | |

#### PUT /2.2/partners/upsells/authentication
Step 2 "Verify". Encoding: **JSON**.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `authentication_key` | string | Trimmed; 1–256 chars; no control characters | The code received |
| `authentication_method` | string | Same as step 1 | |
| `session_id` | string | `^[A-Za-z0-9_.:-]{1,256}$` | Note the different key name from steps 1 and 3 |

#### POST /2.2/partners/upsells/orders
Step 3 "Get offer". Encoding: **JSON**. Confirmation only when ordering: "Place this partner order? The partner may charge you." Response shown as-is; no refresh.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `internal_offer_ids` | string[] | Each `^[A-Za-z0-9_.:-]{1,128}$`; ≥ 1; de-duplicated | The chosen offer's ID |
| `authentication_session_id` | string | `^[A-Za-z0-9_.:-]{1,256}$` | |
| `should_submit` | bool | `false` = price quote (default), `true` = place order | |

#### POST /2.2/partners/encryptme
Older services › encrypt.me VPN › Create account (legacy eero Plus partner). Encoding: **form**, body `test=false` (production partner environment). Confirmation: "Create an encrypt.me VPN account through eero? …" Response shown as-is.

### Settings › Connected services

#### GET /2.2/networks/{network_id}/proxied_nodes
Page data for **Settings › Connected services** (eero Built-in). Field read: `enabled` (boolean). If `enabled` is boolean a switch is shown; otherwise a **Change** button re-reads this endpoint and opens an editor. The page also shows, from the network object: `amazon_account_linked`, `alexa_skill`, `ffs` (booleans, read-only).

#### PUT /2.2/networks/{network_id}/proxied_nodes
**eero Built-in** switch / editor. Body: JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `enabled` | boolean | `true` / `false` | |

#### GET /2.2/networks/{network_id}/simple_setup/vendors
Page data for **Connected services › Connected device setup**. Response list from `data`, `data.vendors` or `data.configurations`.

| Field | Type | Notes |
|---|---|---|
| `vendor_id` | string | If empty, the vendor is shown read-only |
| `vendor_name` / `name` / `display_name` | string | Display name |
| `enabled` | boolean | Switch state |

#### PUT /2.2/networks/{network_id}/simple_setup/vendors/{vendor_id}
Toggling a vendor switch. Confirmation: "Allow / Stop allowing <name> to set up devices on this network?". `{vendor_id}` is URL-escaped. Body: **form**.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `enabled` | boolean | `true` / `false` | |

### Settings › Connected services › Amazon connected home

The current state is read from the network object: `amazon_account_linked` (boolean) and `alexa_skill` (boolean, shown when present). New links can't be made from the desktop app.

#### POST /2.2/amazon/networks/{network_id}/account_unlink
- **Used by:** Amazon account › **Unlink**. Shown only when the account is linked.
- **Confirmation:** "Unlink the Amazon account from this network? ..."
- **Encoding:** none.

#### POST /2.3/networks/{network_id}/shh/start_zigbee_discovery
- **Used by:** Zigbee devices › **Find devices**. Shown only when the Amazon account is linked.
- **Confirmation:** "Start looking for nearby Zigbee devices? ..."
- **Encoding:** none.

### Settings › Connected services › Accessories & cellular

The accessory list comes from the network object's `accessories[]`. For each accessory the app reads:

- `dsn` / `serial`
- `eero_id` (matched to the eero whose `url` ends with this ID)
- `joined` (shows "Connected") or `registered` (shows "Registered, not connected"); otherwise "Not set up"
- `name`, `model`

The network's `cellular_backup` object is shown as details when present.

#### GET {network_url}/accessories/setup/status
- **Used by:** the page load. Shown under technical details.

#### GET {network_url}/accessories/{serial}
- **Used by:** Accessory › **Serial number** › **Details**.
- **Rule:** `{serial}` must match `^[A-Za-z0-9-]{8,40}$` and is URL-escaped.

#### GET {network_url}/sip_config_associations/{eero_id}
- **Used by:** Accessory › **Phone lines** › **View**.
- **Rule:** `{eero_id}` is the accessory's `eero_id` and must match `^[A-Za-z0-9_-]{1,128}$`.

#### DELETE {network_url}/accessories/{serial}
- **Used by:** Accessory › **Remove accessory**.
- **Confirmation:** you must type the exact serial, then confirm "Remove this accessory? ...".
- **Encoding:** none.

### Settings › Network management

#### POST /2.2/networks/{network_id}/transfer (send invitation)
**(resource-overridable: `transfer`)** **Ownership › Transfer ownership › Transfer**. Confirmation required. Body: **form**.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `name` | string | Name rule; trimmed | Recipient name |
| `email` | string | Email rule when method is Email; otherwise sent as empty string | |
| `phone` | string | Phone rule (normalized) when method is Text message; otherwise empty string | |

All three fields are always sent; exactly one of `email` / `phone` is non-empty. Method choice: `Email` (default) or `Text message`.

#### GET /2.2/transfer/has_networks
**Transfer ownership › Check recipient**. Query: either `email=<address>` (input contains `@`; email rule, max 254) or `phone=<+digits>` (phone rule), URL-escaped. Displayed as-is (whether the recipient has an eero account and networks).

#### GET /2.2/networks/{network_id}/transfer
**(resource-overridable: `transfer`)** Page data for **Network management › Outgoing transfer**. Fields read: `recipient_email_address` / `recipient_phone_number` / `recipient_name` (first present = recipient), `transfer_date`. If both recipient and date are empty, "No transfer in progress" is shown.

#### POST /2.2/networks/{network_id}/transfer (resend)
**(resource-overridable: `transfer`)** **Outgoing transfer › Resend**. Confirmation required. Body: none.

#### DELETE /2.2/networks/{network_id}/transfer
**(resource-overridable: `transfer`)** **Outgoing transfer › Cancel**. Confirmation required. Body: none.

#### GET /2.2/network_transfers/pending
Page data for **Network management › Pending transfers** (incoming).

| Query parameter | Type | Notes |
|---|---|---|
| `token` | integer | Optional; used for **Next page of transfers** with the previous response's `next_token` (shown only when `next_token` parses as an integer) |

Response fields: `transfers[]` with `id` (validated identifier), `organization_name` / `sender_name` / `from`, `network_name` / `subset_name` / `name`, `created_at` / `created`; and `next_token`.

#### POST /2.2/network_transfers/{transfer_id}/accept
#### POST /2.2/network_transfers/{transfer_id}/deny
**Pending transfers › Accept / Decline**. Confirmation required. Body: none. After success the client clears all network state, reloads the account and network list, and returns to Home.

#### DELETE /2.2/networks/{network_id}
**Danger zone › Delete network**. The network's `url` is used as the path. The user must type the current network `name` exactly, then confirm. Body: none. Not available in preview mode. On success the client clears network data and reloads the account.

### Settings › Network management › Check recipient

#### GET /2.2/transfer/has_networks?email={email} | ?phone={phone}
Shows whether the recipient already has an eero account and networks. Response shown as-is.

| Query | Type | Values / rules | Notes |
|---|---|---|---|
| `email` | string | Used when the input contains `@`; trimmed, ≤ 254 chars, `^[^\s@]+@[^\s@]+\.[^\s@]+$` | URL-escaped |
| `phone` | string | Otherwise; E.164: characters `+ 0-9 space ( ) . -` only; 10 digits without `+` get `1` prefixed; 11 digits without `+` must start with `1`; 8–15 digits, no leading 0; `+1` numbers need exactly 11 digits | Sent as `+<digits>`, URL-escaped (e.g. `phone=%2B14255550100`) |

### Settings › Network management › Organization & community

#### PUT /2.2/networks/{network_id}/network_customer_type
"Network type". Network-relative. Encoding: **form**. Confirmation: "Change this network's type to <type>? Business and MDU types change which features and plans apply." Current value from `network.network_customer_type`, else `network.network_type`, else `Residential`.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `network_customer_type` | string | `Residential`, `Business`, `MDU` (case-sensitive) | |

#### GET /2.2/networks/{network_id}/community
"Community" › View. Network-relative. Response shown as-is.

#### GET /2.2/networks/{network_id}/cobranding
"Provider branding" › View. Network-relative. Response shown as-is.

#### GET /2.2/managed_summary
"Managed networks" › View. Response shown as-is.

#### GET /2.2/organizations/self/networks/pro_managed[?network_name_filter={text}]
"Professionally managed networks" › Search. `network_name_filter` optional, trimmed, ≤ 64 chars, omitted when empty.

#### GET /2.2/organizations/self/subsets?limit=50[&search={text}]
"Units" › Search. `limit=50` fixed; `search` optional, trimmed, ≤ 64 chars.

#### PUT /2.2/organizations/self/subsets/{subset_id}/associate
"Assign an eero to a unit". `{subset_id}` = unit ID entered by the user (trimmed, `^[A-Za-z0-9_-]{1,128}$`). Encoding: **JSON**. Confirmation: "Assign this eero to the selected unit?"

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `eero_serial` | string | Serial rule | |
| `unit_identifier` | string | Trimmed; 1–64 characters | Unit name, e.g. `4B` |
| `unit_description` | string | Trimmed; 0–200 characters | Sent as `""` when blank |

#### POST /2.2/networks/{network_id}/holding_state
"Danger zone › Holding state › Put on hold". Network-relative. Body **none**. Two confirmations: "Put this network in a holding state? …" then "Are you sure? Continue only if eero or your provider told you to."

### Settings › Advanced networking › Wireless settings

#### GET /2.2/networks/{network_id}/ac_compat
Network-relative. Read when opening "Legacy compatibility mode"; reads `enabled` (bool) to prefill the switch.

#### PUT /2.2/networks/{network_id}/ac_compat
- Encoding: **form**; no confirmation.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `enabled` | bool | `true` / `false` | |

#### PUT /2.2/networks/{network_id}/mlo_mode
Multi-link operation (Wi-Fi 7). Row shown only when `network.mlo_mode` is present. Network-relative.

- Encoding: **JSON**. Confirmation: "Change multi-link operation? Connected devices may reconnect."

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `mlo_mode` | string | `disabled` (Off), `single` (Single link), `multi` (Multi-link) | |

#### PUT /2.2/networks/{network_id}/passpoint/enabled
Passpoint (Hotspot 2.0). Shown only when `network.passpoint` is a boolean or `network.passpoint.enabled` is a boolean. Network-relative.

- Encoding: **JSON**. Confirmation: "Turn on/off Passpoint? Devices may briefly reconnect."

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `enabled` | bool | | |

#### GET /2.2/networks/{network_id}/radio_settings
Reports › Radio settings. Network-relative. Response shown as-is (channel and power per eero radio).

#### GET /2.2/networks/{network_id}/channel_utilization?start={start}&end={end}
Reports › Channel utilization. Network-relative; range is the last 1 day. Response shown as-is.

"Security by radio band" (WPA2/WPA3 per band) is also on this page but is documented with the band security endpoints elsewhere.

### Settings › Advanced networking › Wireless settings › Security by radio band

#### GET /2.2/networks/{network_id}/wpa3_per_band
Read when **Change** is opened. Fields: `band_2_4_ghz`, `band_5_ghz`.

#### PUT /2.2/networks/{network_id}/wpa3_per_band
Saving the editor. Confirmation required. Body: JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `band_2_4_ghz` | string | `WPA2` \| `WPA2_WPA3` \| `WPA3` | |
| `band_5_ghz` | string | `WPA2` \| `WPA2_WPA3` \| `WPA3` | |

6 GHz is not part of this request (always WPA3).

### Settings › Advanced networking › Wireless settings › Fast roaming

#### GET {network_url}/fast_transition
#### PUT {network_url}/fast_transition
- **Used by:** the page load and the **Fast roaming** switch.
  - The switch appears only when the response's `fast_transition` is a boolean.
  - Confirmation: "Turn on/off fast roaming? Some wireless devices may reconnect."
- **PUT encoding:** JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `fast_transition` | boolean | `true` / `false` | 802.11r |

### Settings › Advanced networking › Thread

#### GET /2.2/networks/{network_id}/thread
Loads the page. Resource-aware (`resources.thread`).

Response fields read: `enable_credential_syncing` (bool); credentials shown on request: `active_operational_dataset`, `commissioning_credential`, `master_key` (each only if 1–8192 characters; if none present → "This network did not return Thread credentials for your account."). Whole object also shown as technical details. The Thread on/off state comes from `network.thread`.

#### PUT /2.2/networks/{network_id}/thread
Network-relative. Encoding: **JSON**. Confirmation only when turning Thread off: "Turn off Thread? Thread smart-home devices will stop working until you turn it back on."

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `thread_enable` | bool | | Credential-sharing switch sends the current `network.thread` |
| `enable_credential_syncing` | bool | | Thread switch sends the current value |

#### POST /2.2/networks/{network_id}/thread
Reset Thread credentials. Network-relative. Body: **none**. Confirmation: "Reset this Thread network's credentials? Every Thread device will need to be paired again."

### Settings › Advanced networking › Dynamic DNS

Requires an eligible subscription (server returns 402 otherwise). Page reads `network.dynamic_dns.enabled` (bool; if not a bool, separate Turn on / Turn off buttons are shown) and `network.dynamic_dns.hostname` / `host` / `domain` (shown with a Copy button).

#### PUT /2.2/networks/{network_id}/ddns/enable
Network-relative. Body: **none**. No confirmation.

#### PUT /2.2/networks/{network_id}/ddns/disable
Network-relative. Body: **none**. Confirmation: "Turn off Dynamic DNS? Anything using your hostname will stop reaching your network."

### Settings › Advanced networking › Reservations & port forwarding

The page is not shown (and no requests are made from it) when `network.connection.mode` is `bridged` or `ipPassthrough`.

#### GET /2.2/networks/{network_id}/routing
Loads the page. Resource-aware (`resources.routing`).

Response fields read: `reservations`, `forwards`, `pinholes` — each either an array or an object with a `data` array and optionally a `url` (the collection URL used for creating entries).
- Reservation items: `url`, `description`, `mac`, `ip`.
- Forward items: `url`, `description`, `ip`, `gateway_port`, `client_port`, `protocol`, `enabled`.
- Pinhole items: `url`, `device` (a device `url`), `protocol`, `port`.

Collection URL for creation: `routing.<kind>.url`; fallback for reservations/forwards is resource-aware `{network_url}/reservations` / `{network_url}/forwards`, for pinholes `{network_url}/ipv6/pinholes`. Edits and deletes use the item's own `url` (error if missing).

#### POST /2.2/networks/{network_id}/reservations
#### PUT {reservation_url}
Reserve an address / edit a reservation. Encoding: **JSON**. No confirmation.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `description` | string | Trimmed; no length check | Nickname |
| `mac` | string | Normalised MAC (`02:00:00:00:00:01`) | |
| `ip` | string | IPv4 dotted quad | |

#### DELETE {reservation_url}?delete_forwards=true
Body **none**. Confirmation: "Delete this reservation and its port forwards?"

#### POST /2.2/networks/{network_id}/forwards
#### PUT {forward_url}
Open a port / edit a port forward. Encoding: **JSON**. Confirmation: "Save this port forward? It allows matching inbound internet traffic to the selected device."

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `ip` | string | IPv4 dotted quad | Device address |
| `gateway_port` | string | `N` or `N-M`, 1–65535, `M ≥ N`; single port when N = M | Internet-side port/range |
| `client_port` | string | Same format; must contain the same number of ports as `gateway_port` | Device-side port/range |
| `protocol` | string | `tcp`, `udp`, `both` | Default `tcp` |
| `description` | string | Trimmed | |
| `enabled` | bool | Default `true` for new forwards | |

Example: `{"ip":"192.168.4.20","gateway_port":"8000-8010","client_port":"9000-9010","protocol":"both","description":"Example","enabled":true}`

#### DELETE {forward_url}
Body **none**. Confirmation: "Delete this rule?"

#### POST /2.2/networks/{network_id}/ipv6/pinholes
#### PUT {pinhole_url}
IPv6 firewall rule. Encoding: **JSON**. Confirmation: "Allow this inbound IPv6 traffic to the selected device?" Requires at least one device with a `url`.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `device` | string | A device `url` chosen from the network's device list | |
| `protocol` | string | `tcp`, `udp`, `both` | |
| `port` | string | `N` or `N-M`, 1–65535, ascending | |

#### DELETE {pinhole_url}
Body **none**. Confirmation: "Delete this rule?"

### Settings › Advanced networking › Power saving

Page reads `network.power_saving` and `network.power_saving_schedule_enabled`.

#### PUT /2.2/networks/{network_id}/power_saving
Network-relative. Encoding: **JSON**. No confirmation.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `enable` | bool | | Schedule switch sends the current value |
| `power_saving_schedule_enabled` | bool | | Power-saving switch sends the current value |

#### GET /2.2/networks/{network_id}/power_saving/schedules
Loads the schedule list. Network-relative. Response: array or `{ schedules: [...] }` / `{ data: [...] }`. Item fields read: `url` (fallback `{network_url}/power_saving/schedules/{id or schedule_id}`), `id`/`schedule_id`, `name`, `days`, `start_time`, `end_time`, `enabled`.

#### POST /2.2/networks/{network_id}/power_saving/schedules
#### PUT {power_schedule_url}
Encoding: **JSON**. No confirmation.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `name` | string | Trimmed; 1–100 characters | |
| `days` | string[] | Subset of `SUN`,`MON`,`TUE`,`WED`,`THU`,`FRI`,`SAT`; at least one; sent in that order | |
| `start_time` | string | 24-hour `HH:mm` (two-digit hour) | Default `22:00` |
| `end_time` | string | `HH:mm`; must differ from start | Default `07:00`; earlier than start = continues to next day |
| `enabled` | bool | Default `true` for new schedules | |

#### DELETE {power_schedule_url}
Body **none**. Confirmation: "Delete this power saving schedule?"

### Settings › Advanced networking › Network flags

Page lists `network.flags` (boolean entries as switches, others as read-only values) and shows `network.temporary_flags` as cards.

#### PUT /2.2/networks/{network_id}/flags/{flag_name}
Network-relative. Encoding: **form**. Confirmation: "Set network flag <name>? Unknown flags can change how the network behaves."

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `{flag_name}` (path) | string | `^[a-z0-9_]{1,64}$` | e.g. `custom_whitelist_blacklist` |
| `value` | string | Trimmed; 1–512 characters | Boolean switches send `true` / `false` |

### Settings › Advanced networking › Internet backup

#### GET /2.2/networks/{network_id}/backupinternet
Page data (part 1, "status"). Field read: `backup_internet_enabled` (boolean). The whole object is also shown under technical details.

#### GET /2.2/networks/{network_id}/backup_access_points
Page data (part 2). List (from `data` or `data.access_points`).

| Field | Type | Notes |
|---|---|---|
| `ssid` | string | Backup wifi name |
| `enabled` | boolean | Shown "Off" only when explicitly `false` |
| `uuid` | string | Identifier used in edit/remove/reorder |

#### PUT /2.2/networks/{network_id}/backupinternet
**Internet backup** switch. Body: JSON `{"backup_internet_enabled": true|false}`.

#### POST /2.2/networks/{network_id}/backup_access_points/connectivity_check
**Test backup connection › Test**. Body: none. Response not read; no refresh. Not available in preview mode.

#### POST /2.2/networks/{network_id}/backup_access_points (add)
**Add a backup network › Add** (also used from **Find nearby** with the discovered SSID pre-filled). Body: JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `ssid` | string | SSID rule; trimmed | |
| `uuid` | string | Newly generated GUID (lower-case, hyphenated) | Created by the client |
| `password` | string | Wifi-password rule; required when adding | |

`enabled` is **not** sent when adding (the "Enabled" switch only applies to edits).

#### PUT /2.2/networks/{network_id}/backup_access_points/{uuid} (edit)
**Edit** on a backup network row. Body: JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `ssid` | string | SSID rule; trimmed | |
| `uuid` | string | Existing `uuid` | |
| `enabled` | boolean | `true` / `false` | |
| `password` | string | Wifi-password rule | Only sent if a new password was typed; blank keeps the current one |

#### DELETE /2.2/networks/{network_id}/backup_access_points/{uuid}
**Remove** on a backup row. Confirmation required. Body: none.

#### POST /2.2/networks/{network_id}/backup_access_points/rearrange
**Backup order › Change** (shown when more than one backup network exists; requires at least two). Body: JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `rearranged_ids` | array of string | Every saved backup `uuid` exactly once (no duplicates, not empty); each a validated identifier | Highest priority first |

#### PUT /2.3/networks/{network_id}/devices/secondary_wan_config
**Devices allowed on backup › Choose**. One switch per device with a `mac` (from the network's device list; label `nickname` / `hostname` / `display_name` / `manufacturer` + MAC). Initial switch state = NOT `secondary_wan_deny_access`. Only changed devices are sent; confirmation "Update backup internet access for N device(s)?". **One request per changed device**, sequentially. Version 2.3 is fixed; `{network_id}` validated. Body: JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `mac` | string | 12 hex digits (separators `-` `:` `.` space allowed in input), sent as lower-case `aa:bb:cc:dd:ee:ff` | |
| `secondary_wan_deny_access` | boolean | `true` = device may NOT use backup; `false` = allowed | Inverse of the switch |

Refresh happens only after the last request.

#### GET /2.2/networks/{network_id}/cellular_backup_usage
**Cellular backup usage › View**. Displayed as-is.

#### GET /2.2/networks/{network_id}/cellular_backup_events
**Cellular backup history › View**. Displayed as-is.

### Settings › Advanced networking › Internet backup (discovery and order)

#### POST {network_url}/backup_access_points/ssid_discovery
#### GET {network_url}/backup_access_points/ssid_discovery
- **Used by:** Find backup wifi › **Search** (POST, no body, no refresh) and **Show results** (GET, which is also the page load).
- **Response read:**
  - `status`, for example `SUCCESS`, which shows as "Done".
  - `discovered_ssids[]` with `ssid` and `rssi`. Results are sorted by `rssi`, strongest first. The labels are: -60 and above "Strong", -70 and above "Good", -80 and above "Weak", lower "Very weak".
- **Use as backup:** the **Use as backup** button opens the add-backup-network editor with the SSID filled in. That editor is documented with the Internet backup settings.

#### POST {network_url}/backup_access_points/rearrange
- **Used by:** Internet backup › backup wifi priority › **Save priority**.
- **Rule:** at least two saved backup networks are required.
- **Encoding:** JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `rearranged_ids` | array of string | Every saved backup network's `uuid` exactly once, highest priority first. Each must match `^[A-Za-z0-9_-]{1,128}$`. An empty list or duplicates are rejected. | Example: `{"rearranged_ids":["backup-2","backup-1"]}` |

### Settings › Advanced networking › Multi-WAN

#### GET /2.2/networks/{network_id}/wan/links
Page data.

| Field | Type | Notes |
|---|---|---|
| `wan_mode` / `mode` | string | `MULTI_WAN_AUTOMATIC` (Automatic failover), `MULTI_WAN_STRICT` (Strict), `MULTI_WAN_LEGACY` (Legacy) |
| `links[]` (or a bare list) | array | Entries without `link_id` are skipped |
| `links[].link_id` | string | |
| `links[].name` | string | |
| `links[].priority` | integer | Lower = used first |
| `links[].connection_type` | string | Shown |
| `links[].metadata` | any | Passed back unchanged on priority edits |

#### PUT /2.2/networks/{network_id}/wan/enable_multi_wan
**Routing mode › Change**. Confirmation required ("internet may drop briefly"). Body: JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `wan_mode` | string | `MULTI_WAN_AUTOMATIC` \| `MULTI_WAN_STRICT` \| `MULTI_WAN_LEGACY` | Default selection Automatic failover when current mode is unknown |

#### GET /2.2/networks/{network_id}/wan/discover
**Find connections › Find**. Displayed as-is.

#### GET /2.2/networks/{network_id}/wan/limits
**Connection limits › View**. Displayed as-is.

#### GET /2.2/networks/{network_id}/wan/links/{link_id}
**Details** on a connection row. Displayed as-is.

#### DELETE /2.2/networks/{network_id}/wan/links/{link_id}
**Remove** on a connection row. Confirmation required. Body: none.

#### POST /2.2/networks/{network_id}/wan/links (add)
**Add a connection › Add**. Requires at least one eero with a `url`. Confirmation: "Add this <type> internet connection?". Body: JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `name` | string | Name rule; trimmed | e.g. "Fiber" |
| `eero_id` | integer | Last segment of the chosen eero's `url`, must parse as integer | Gateway eero choice |
| `port_number` | integer | 0–48 | Ethernet port |
| `priority` | integer | 0–100; default `1` | Lower used first |
| `connection_type` | string | `DHCP` (default) \| `STATIC_IP` \| `PPPOE` | |
| `static_ip` | object | Only when `STATIC_IP` | see below |
| `static_ip.static_ip` | string | IPv4 rule | |
| `static_ip.subnet_mask` | string | IPv4 rule | |
| `static_ip.gateway_ip` | string | IPv4 rule | |
| `pppoe` | object | Only when `PPPOE` | |
| `pppoe.username` | string | required, non-empty (not trimmed) | |
| `pppoe.password` | string | required, non-empty | |
| `vlan_id` | **string** | Optional; integer 1–4094, sent as a decimal string | Omitted when blank |

#### PUT /2.2/networks/{network_id}/wan/links (rename / reprioritize)
**Edit** on a connection row. The user sets the connection name and its position (1 = highest, up to the number of links). The client rebuilds the whole ordered list and renumbers priorities as `baseline + index`, where `baseline` is the lowest current priority (or 0). Confirmation required. Body: JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `links` | array | All links, new order | |
| `links[].link_id` | string | | |
| `links[].name` | string | Name rule (edited link, trimmed); others unchanged | |
| `links[].priority` | integer | `baseline + position index` | |
| `links[].metadata` | any | Included only if the link had `metadata` | Unchanged |

### Settings › Advanced networking › Internet connection › ISP settings (PPPoE / VLAN)

Reads from the network object: `pppoe_enabled` (boolean), `pppoe_username` (string), `vlan` (string).

#### PUT /2.2/networks/{network_id}/settings (ISP settings (PPPoE / VLAN))
**(resource-overridable: `settings`)** **ISP settings › Apply ISP settings**. Confirmation required. Body: JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `pppoe` | object | always sent | |
| `pppoe.enabled` | boolean | | "Use PPPoE login" |
| `pppoe.credentials` | object | Sent only when `enabled` is true AND "Enter a new PPPoE username and password" is on | |
| `pppoe.credentials.username` | string | Not blank; trimmed | |
| `pppoe.credentials.password` | string | Not empty; sent as typed | |
| `vlan` | string | `""` (blank = VLAN tagging off) or integer 0–4094 as decimal string | Always sent |

Rules: enabling PPPoE when it was previously off requires new credentials. When credentials are not replaced, no `credentials` object is sent (eero keeps the stored ones). Examples: `{"pppoe":{"enabled":true,"credentials":{"username":"user","password":"…"}},"vlan":"100"}`; `{"pppoe":{"enabled":false},"vlan":""}`.

### Settings › Advanced networking › DNS › DNS performance

#### POST /2.2/networks/{network_id}/dns_perf_test
**Run a test › Start test**. Pre-filled with the network's `dns.custom.ips`. Body: JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `servers` | array | 1–8 entries; input split on newlines, `,` and `;`, trimmed, de-duplicated | |
| `servers[].ip` | string | IPv4 or IPv6 literal (hostnames/URLs rejected), canonical form | |
| `servers[].port` | integer | 1–65535; default `53`; same port for every server | |

Response: `action_uuid` (validated identifier) — remembered and used to fetch results. The page then reads the result.

#### GET /2.2/networks/{network_id}/dns_perf_test/{action_uuid}
Page data once a test was started; **Show results** re-reads it. Displayed as-is.

### Settings › Advanced networking › Multiple static IPs

#### GET /2.3/networks/{network_id}/multistaticip
Page data. Version 2.3 fixed; `{network_id}` = last segment of the network url (not otherwise validated).

| Field | Type | Notes |
|---|---|---|
| `enabled` | boolean | |
| `type` | string | `PublicLAN` (default when missing) or `NATWithPortFwd` |
| `multistaticip_settings.subnet_ip` / `.subnet_mask` / `.router_ip` | string | PublicLAN mode |
| `multistaticip_settings_nat_portfwd.subnet_ip_start` / `.subnet_ip_end` | string | NAT mode |

#### PUT /2.3/networks/{network_id}/multistaticip
**Multiple static IPs › Change**. Confirmation required. Body: JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `enabled` | boolean | | |
| `type` | string | `PublicLAN` ("Public addresses on devices") \| `NATWithPortFwd` ("Port forwarding (NAT)") | Always sent, even when disabled |
| `multistaticip_settings` | object | Only when enabled and `PublicLAN` | |
| `…subnet_ip` | string | IPv4 rule | Public subnet address |
| `…subnet_mask` | string | IPv4 rule | |
| `…router_ip` | string | IPv4 rule | ISP router address |
| `multistaticip_settings_nat_portfwd` | object | Only when enabled and `NATWithPortFwd` | |
| `…subnet_ip_start` | string | IPv4 rule | First public IP |
| `…subnet_ip_end` | string | IPv4 rule | Last public IP |

### Settings › Additional wifi networks

#### GET /2.2/networks/{network_id}/subnets_config
Page data for **Settings › Additional wifi networks** and **Home › Security & privacy › Content filters**. List from `data`, `data.subnets` or `data.configurations`.

| Field | Type | Notes |
|---|---|---|
| `subnet_type` | string | Slot: `A` or `B` (only these can be deleted; the first free one is used for Add) |
| `subnet_kind` | string | e.g. `IoT`, `Kids`, `Business`, `Guest` |
| `subnet_id` | string | Used for content filters (falls back to `subnet_type`) |
| `name` | string | Wifi name |
| `enabled` | boolean | |
| `open_network` | boolean | |
| `wan_access` | boolean | |
| `rate_limit_pct` | integer | <100 shown as a bandwidth limit |
| `password` | string | Used as the kept password when editing without typing a new one |
| `network_id`, `dedicated_subnet` | any | Echoed back on edit |

#### PUT /2.2/networks/{network_id}/subnets_config
**Add a wifi network › Add** (only when slot A or B is free) and **Edit**. Confirmation required. Body: JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `name` | string | SSID rule; trimmed | |
| `enabled` | boolean | default `true` for new | |
| `open_network` | boolean | default `false` for new | |
| `wan_access` | boolean | default `true` for new | "Allow internet access" |
| `rate_limit_pct` | integer | 1–100; default `100` (no limit) | |
| `password` | string | Wifi-password rule; omitted when `open_network` is `true` | On edit, blank = existing `password` |
| `network_id` | string | New: last segment of the network url. Edit: echoed from the subnet if present | |
| `subnet_type` | string | New: free slot `A` or `B`. Edit: echoed | |
| `subnet_kind` | string | New: `IoT` (default) \| `Kids` \| `Business` \| `Guest`. Edit: echoed | |
| `subnet_id` | any | Edit only, echoed if present | |
| `dedicated_subnet` | any | Edit only, echoed if present | |

#### DELETE /2.2/networks/{network_id}/subnets_config/{subnet_type}
**Delete** (only for `A` or `B`). Confirmation required. Body: none.

#### GET /2.2/networks/{network_id}/subnets_config/{subnet_id}/dns_policies/content_filters
**Content filters** / **Edit filters** on a subnet. `{subnet_id}` = `subnet_id` or `subnet_type`, URL-escaped. The client collects boolean keys from any of: `dns_policies`, `unified_content_filters.dns_policies`, `premium_dns.dns_policies`, `premium_dns.unified_content_filters.dns_policies` (later sources override earlier ones). If none, an error is shown.

#### PUT /2.2/networks/{network_id}/subnets_config/dns_policies/content_filters
Saving the subnet content filter editor. Body: JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `subnets` | array of string | one element: the subnet id used above | |
| `content_filters` | object | every collected key → boolean | All keys sent together |

### Settings › Business guest wifi (captive portal)

#### GET /2.2/networks/{network_id}/captive_portal/configurations
Page data. List from `data` or `data.configurations`.

| Field | Type | Notes |
|---|---|---|
| `configuration_id` / `id` | integer | Must parse as integer for edits |
| `header`, `sub_header` | string | |
| `color_theme` | string | `Light` (default) / `Dark` |
| `access_expiration_in_minutes` | integer | Shown as days/hours/minutes |
| `capture_email`, `capture_name` | boolean | |
| `business_logo_url`, `business_terms_of_service_url` | string | "Added" when non-empty |

#### PUT /2.2/networks/{network_id}/captive_portal/configuration (visitor fields)
**Ask for visitor email** / **Ask for visitor name** switches. Body: JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `id` | integer | configuration id | |
| `capture_email` | boolean | | New value or current value |
| `capture_name` | boolean | | New value or current value |

#### PUT /2.2/networks/{network_id}/captive_portal/configuration (branding)
**Heading & theme › Change** / **Session length › Change**. Body: JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `id` | integer | configuration id | |
| `header` | string | free text (not trimmed) | |
| `sub_header` | string | free text | |
| `color_theme` | string | `Light` \| `Dark` | |
| `access_expiration_in_minutes` | integer | 1–525600 | e.g. 60 = 1 hour, 1440 = 1 day |
| `business_logo_url` | string | empty, or absolute `https` URL without user-info | |
| `business_terms_of_service_url` | string | empty, or absolute `https` URL without user-info | |

#### PUT /2.2/networks/{network_id}/captive_portal/subnet_id/GUEST
**Use for guest wifi › Use**. Confirmation required. Body: JSON `{"captive_portal_configuration_id": <integer>}`.

#### GET /2.2/networks/{network_id}/file/import/upload_link
Step 1 of **Logo › Upload** / **Terms of service › Upload**.

| Query parameter | Type | Values | Notes |
|---|---|---|---|
| `importJobType` | string | `CaptivePortalLogo` \| `CaptivePortalToS` | |
| `fileExtension` | string | `png`, `jpg`, `jpeg` (logo) or `pdf` (terms), lower-case, from the file name | |

File checks before any request: logo ≤ 5 MB, PNG (signature `89 50 4E 47 0D 0A 1A 0A`, extension `.png`) or JPEG (`FF D8 FF`, extension `.jpg`/`.jpeg`); terms ≤ 10 MB, PDF (`%PDF-`, extension `.pdf`); minimum 8 bytes. Confirmation shows file name and size. The configuration id must be a validated identifier.

Response fields: `signed_upload_url`, `kms_key_id`, `import_file_path`.

#### PUT {signed_upload_url} (storage upload, not the eero API)
Step 2. The file bytes are sent with `PUT` to `signed_upload_url`, which must be `https`, port 443, no user-info or fragment, host matching `^(?:[a-z0-9][a-z0-9.-]*\.)?s3(?:[.-][a-z0-9-]+)?\.amazonaws\.com$`. Headers: `x-amz-server-side-encryption: aws:kms`, `x-amz-server-side-encryption-aws-kms-key-id: <kms_key_id>` (1–2048 chars, no control characters), `Content-Type: image/png | image/jpeg | application/pdf`. No eero cookie, no redirects followed, 120 s timeout. Any non-2xx status (including redirects) aborts before step 3.

#### POST /2.2/networks/{network_id}/file/import/job
Step 3. Body: JSON.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `import_file_path` | string | from step 1; non-blank, ≤ 4096 chars | |
| `import_job_type` | string | `CaptivePortalLogo` \| `CaptivePortalToS` | |
| `metadata` | string | a JSON-encoded string: `{"configuration-id":"<id>"}` | Note: a string containing JSON, not a nested object |

Example: `{"import_file_path":"uploads/test/logo.png","import_job_type":"CaptivePortalLogo","metadata":"{\"configuration-id\":\"5\"}"}`

### Settings › Business guest wifi › Other networks

#### PUT /2.2/networks/{network_id}/business_guest_network
"Use on my other networks". Network-relative. Encoding: **JSON**. No confirmation.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `enable_for_other_networks` | bool | Default `false` in the dialog | |

### Settings › Business guest wifi › eero Insight

#### GET /2.2/login/temporary (eero Insight)
- **Used by:** eero Insight › **Open**.
- **Query:** none.
- **Response read:** `temporary_token`. The app opens `https://insight.eero.com/login/mobile?next=/networks/{network_id}` and sends the token in the header `x-temp-user-token`.
- **Gating:** you need a business account with permission. This is enforced by the server and by the "Business networks only" badge.

## 9. Help, support and chat

### Help › Report an issue

#### POST /2.2/zendesk/create
Encoding: **multipart/form-data**; every text part is `text/plain; charset=utf-8`. Confirmation: "Send this report to eero support?" No refresh.

Parts in order:

| Part | Type | Values / rules | Notes |
|---|---|---|---|
| `affected_eeros[0]`, `affected_eeros[1]`, … | string | eero serials (serial rule), distinct | One part per ticked eero |
| `affected_devices[0]`, … | string | Normalised MACs, distinct | Only the first 40 devices are offered |
| `labels[0]`, … | string | Each `^[A-Za-z0-9_.:-]{1,64}$`; from a comma/newline separated "Tags" field; distinct | Optional |
| `issue_timestamp` | string | UTC `yyyy-MM-ddTHH:mm:ssZ` (time of sending) | |
| `description` | string | Trimmed; 10–8000 characters | |
| `summary` | string | Trimmed; 3–200 characters | |
| `network_id` | string | `{network_id}`, or `""` when no network is selected | |
| `reportscreen` (file) | `image/png` | Optional; PNG signature required; ≤ 8 MB; filename = original file name with characters outside `A-Za-z0-9_.-` replaced by `_` | |

### Help › Contact support

#### GET /2.2/account/eero_support

Used by the "Contact support" page and by "Your support details".

| Response field | Use |
|---|---|
| `contacts[]` | Contact list |
| `contacts[].type` | `CHAT`, `HELP_LINK`, `PHONE`, `EMAIL` (others are shown as copyable text) |
| `contacts[].title` | Label (default "Support") |
| `contacts[].content` | URL, phone number or email address |

For `CHAT` and `HELP_LINK`, the `content` must be an HTTPS URL on port 443 with no user-info. It then opens in the embedded browser in provider-navigation mode (see [Embedded browser rules](#embedded-browser-rules)). Other types, or URLs that fail this check, are shown with a Copy button.

### Home › Chat › Warm-up

#### POST /2.2/conversations/warmup
Sent when the chat page opens (not when viewing history), only while signed in with a network selected. Prepares the chat service before the first message; errors other than session expiry are ignored. Encoding: **JSON**.

```json
{ "context": { "focused_network_id": "<network_id>", "client_capabilities": { "opt_in_widgets": [] } } }
```

### Home › Chat (eero assistant)

All chat requests use the session cookie. `{network_id}` is the selected network ID (see [Account, dashboard and notifications](#account-dashboard-and-notifications)).

**Chat availability errors** (`meta.error`):

| Key | Message |
|---|---|
| `error.conversations.not_available` | eero has not enabled chat for this account or network |
| `error.conversations.user_blocked` | eero has restricted chat access for this account |

#### Shared message body

The same body is used by `warmup` and `messages`.

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `context` | object | Always present | |
| `context.focused_network_id` | string | Selected network ID; required (non-blank) | |
| `context.client_capabilities` | object | | |
| `context.client_capabilities.opt_in_widgets` | string[] | Always `[]` | The client opts in to no extra widgets |
| `conversation_id` | string | Omitted for a new conversation | Taken from stream events or history |
| `message` | string | 1 to 8000 characters, not whitespace-only; trimmed by the UI | Omitted on warm-up and on permission answers |
| `mcp_token` | string | `access_token` from `POST /2.2/conversations/token` | Omitted if no token was obtained |
| `permission_grant` | object | `{ "approved": bool, "interrupt_id": string }` | Sent only to answer a permission request |
| `ui_event` | object | | Supported in the body format but never sent by this client |

#### GET /2.2/conversations?networkId={network_id}&excludeEmpty=true

| Query param | Value |
|---|---|
| `networkId` | Selected network ID (URL-escaped) |
| `excludeEmpty` | `true` |

| Response field | Use |
|---|---|
| `conversations[].conversation_id` | Required (rows without it are skipped) |
| `conversations[].description` | Title |
| `conversations[].created` | Title fallback, or subtitle when a description exists |

#### GET /2.2/conversations/{conversation_id}

The `conversation_id` is URL-escaped.

| Response field | Use |
|---|---|
| `conversation_id` | Current conversation (falls back to the requested ID) |
| `message_history[]` | Items in order |
| `message_history[].message_id` | Key for feedback buttons |
| `message_history[].message.sender` | `USER` means the user; anything else is eero |
| `message_history[].message.content` | Text |
| `message_history[].permission_request.friendly_sentence` (or `.content`) | Shown as "eero asked: …" |
| `message_history[].permission_grant.approved` | Shown as approved or declined |
| `message_history[].widget_event` | Rendered as a card (fields below) |

#### GET /2.2/conversations/recommended?networkId={network_id}

| Response field | Use |
|---|---|
| `recommendations[].suggested_prompt` | Text sent when the chip is clicked (required) |
| `recommendations[].title`, then `cta_text` | Chip label (falls back to the prompt) |

If no recommendations are returned, four built-in starter prompts are shown.

#### POST /2.2/conversations/token

| Item | Detail |
|---|---|
| Encoding | JSON |
| When | Before every submitted turn, including permission answers |

| Field | Type | Values / rules |
|---|---|---|
| `refresh_token` | string | Always `""` |

| Response field | Use |
|---|---|
| `access_token` | Sent as `mcp_token`. Kept in memory only. |

If this call fails for any reason other than a session expiry, the turn is sent without `mcp_token`.

#### PUT /2.2/conversations/messages (streaming)

| Item | Detail |
|---|---|
| Auth | `Cookie: s=<session>` |
| Encoding | JSON (shared body) |
| Response handling | The body is read as a stream once headers arrive. The 30 s API timeout covers the wait for headers. Each turn has a 3-minute overall limit, and the user can stop it. |
| Refresh | If the response before streaming starts is 401 `error.session.refresh`, `login/refresh` runs once and the request is resent. |
| Errors | A non-2xx status is an error, using the HTTP status and `meta.error` from the JSON body if it parses. |
| Cookie rotation | `Set-Cookie s=` on a 2xx response replaces the session |

**Stream format.** This is **not** Server-Sent Events and not newline-delimited JSON. The body is a series of complete JSON objects written one after another. Whitespace between objects is allowed, and a newline is common. Each object must begin with `{`, so any other first character, such as HTML, is rejected. Object boundaries are found by brace and bracket depth, ignoring braces inside JSON strings, with escapes honored. The stream is decoded as strict UTF-8.

| Limit | Value |
|---|---|
| Single object | 1 MiB (1,048,576 characters) |
| Whole reply | 8 MiB |
| Truncated final object | Error: "The chat response was interrupted. Reload history before sending again." |

**Stream event fields:**

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `conversation_id` | string | | Updates the current conversation |
| `type` | string | `InitialMessage`, `MessageDelta`, `ToolUse`, `Reasoning`, `Error` | `InitialMessage` sets the text of the bubble. `MessageDelta` appends to it. `ToolUse` and `Reasoning` show "eero is checking your network…". `Error` aborts the reply. |
| `message_id` | string | | Groups deltas into one bubble (`stream-reply` if absent) |
| `content` | string | | Text or delta |
| `finished` | boolean | | Shows feedback buttons for `message_id` |
| `permission_request` | object | | Asks the user to approve an action. The client **stops reading the stream** and waits for the user's decision. |
| `permission_request.interrupt_id` | string | Required | Echoed in `permission_grant.interrupt_id` |
| `permission_request.friendly_sentence` | string | | Question text |
| `permission_request.tool_descriptions` | string[] | | Bulleted list of what will run |
| `widget_event` | object | | Card shown inline |

**Permission answers.** "Approve" needs a second Windows Yes/No confirmation. "Decline" is sent straight away. Either way the client calls `POST /2.2/conversations/token`, then sends `PUT /2.2/conversations/messages` with `conversation_id`, `mcp_token` and `permission_grant: {"approved": true|false, "interrupt_id": "<id>"}` and no `message`. Each `interrupt_id` can be answered only once.

**Widget event fields read:**

| Field | Use |
|---|---|
| `instance_id` | Replaces an earlier card with the same ID |
| `action` | `remove` or `delete` (case-insensitive) removes the card |
| `content_summary`, then `properties.prompt_text`, `properties.title`, `properties.description`, then `widget_type` | Card title |
| `properties.{name, location, model, status, connected, connection_type, connection_state, device_type, is_guest, is_private, score_bars, mesh_quality_bars, wan_type, eero, port_number, negotiated_speed, total_devices, paused_devices, accessory_type, bars, cellular_backup_status, configuration_status}` | Detail rows (booleans shown as Yes or No) |
| `properties.contacts[].title`, `.content` | Contact rows |
| `properties.choices[]` | Strings. On live cards only, each becomes a chip that sends that text as a new message. |

#### PUT /2.2/conversations/feedbacks

| Item | Detail |
|---|---|
| Encoding | JSON |
| Gating | Once per message (the buttons are disabled after use) |

| Field | Type | Values / rules |
|---|---|---|
| `message_id` | string | ID of an eero reply |
| `positive` | boolean | `true` = Helpful, `false` = Not helpful |

## 10. Billing and payments

### Billing and payment hand-off

#### GET /2.2/premium/customer (Billing and payment hand-off)

| Item | Detail |
|---|---|
| When | Opening "Billing & status", the Refresh button, a subscription Details view, and automatically after any billing window closes (if the same session is still signed in) |

| Response field | Use |
|---|---|
| `plan`, then `plan_name`, then `name` | Plan name |
| `status` | Plan status |
| `payment_channel` | `eero` means billed by eero (change payment with Manage). Any other value means billed through that store or provider. |

After a refresh the network is reloaded too.

#### GET /2.2/login/temporary?client_type=WEB_PAYMENTS&target_page={target_page}

Gets a short-lived web token to open eero's payment pages already signed in.

| Query param | Type | Values / rules |
|---|---|---|
| `client_type` | string | `WEB_PAYMENTS` |
| `target_page` | string | `MANAGE_SUBSCRIPTION` (plan and payment method), `MANAGE_PURCHASES` (receipts and purchase history), `MOBILE_ADDON_PAYMENT` (add-ons) |

| Response field | Use / validation |
|---|---|
| `target_url` | Must be absolute `https`, port 443, no user-info, with host `eero.com` or a subdomain of `eero.com`. Otherwise it is refused. |
| `temporary_token` | 1 to 8192 printable ASCII characters (33 to 126). Sent only as a request header (below). |

**How the token is passed and confined:** the billing window opens `target_url` with one extra request header, `x-temp-user-token: <temporary_token>`, on the first navigation. Every later request from that page is checked. The header is removed unless the request URL is HTTPS on port 443 with no user-info and its absolute URI is **exactly** the original `target_url`. The token is never placed in the URL and never sent to another host or path. The window is a private (in-memory) browser profile with password saving turned off.

#### GET /2.2/organizations/self/payment_account/management_url

Business organizations only. Manages an existing payment account.

| Response field | Use |
|---|---|
| `external_payment_account_management_url` | Opened in the billing window. It must pass the embedded-browser allowlist. |

#### POST /2.2/organizations/self/payment_account

Sets up a business payment account.

| Item | Detail |
|---|---|
| Body | None |
| Gating | Not available in sample mode. The user must confirm first. |

| Response field | Use |
|---|---|
| `external_payment_account_onboard_url` | Opened in the billing window. It must pass the embedded-browser allowlist. |

#### Billing completion callbacks

While a billing window is open, any navigation to one of these addresses is intercepted. The navigation is cancelled and the window closes. Subscription status is then reloaded from `GET /2.2/premium/customer`. The callback itself is never trusted as proof of purchase.

| Pattern | Notes |
|---|---|
| `eero://jumpto/{page}` | |
| `https://app.eero.com/{page}` | Port 443, no user-info |
| `https://app.eero.com/{page}/index.html` | |
| `https://app.eero.com/jumpto/{page}` | |

`{page}` is one of `pluspurchasecomplete`, `businesspurchasecomplete`, `datapackpurchasecomplete`. Any other page, or a look-alike host such as `app.eero.com.evil.test`, is not a callback.

Billing windows use provider-navigation mode (see [Embedded browser rules](#embedded-browser-rules)), so card-issuer and 3-D Secure pages on any HTTPS host can load.

## 11. Outside services

### Sign-in through outside services

**Amazon consumer sign-in**

#### Browser: GET https://www.amazon.com/ap/signin?{query}

Opened in a dedicated private embedded-browser window. Each attempt gets a fresh, uniquely named profile, and password saving is off.

**Per-attempt values:**

| Value | How it is made |
|---|---|
| `code_verifier` | 32 random bytes, base64url without padding (43 characters) |
| `state` | 32 random bytes, base64url without padding |
| Virtual device client | A random 32-character uppercase hex serial and a fixed Amazon device-type identifier, joined as `<serial>#<device type>`. The UTF-8 bytes are then written as lowercase hex. A new one is made for every attempt. |
| `code_challenge` | `BASE64URL(SHA-256(ASCII(code_verifier)))`, method **S256** (RFC 7636) |

**Query parameters**, each key and value percent-escaped:

| Parameter | Value |
|---|---|
| `openid.assoc_handle` | `amzn_eero_mobile_android_us` |
| `pageId` | `eero_mobile_accounts` |
| `language` | `en_US` |
| `openid.ns` | `http://specs.openid.net/auth/2.0` |
| `openid.mode` | `checkid_setup` |
| `openid.claimed_id` | `http://specs.openid.net/auth/2.0/identifier_select` |
| `openid.identity` | `http://specs.openid.net/auth/2.0/identifier_select` |
| `openid.ns.pape` | `http://specs.openid.net/extensions/pape/1.0` |
| `openid.pape.max_auth_age` | `0` |
| `openid.ns.oa2` | `http://www.amazon.com/ap/ext/oauth/2` |
| `openid.oa2.response_type` | `code` |
| `openid.oa2.scope` | `device_auth_access` |
| `openid.oa2.client_id` | `device:` followed by the virtual device client (hex) |
| `openid.oa2.code_challenge` | PKCE challenge |
| `openid.oa2.code_challenge_method` | `S256` |
| `openid.return_to` | `https://www.amazon.com/ap/maplanding?state=<state>` |
| `accountStatusPolicy` | `P1` |

No `client_id` (developer OAuth client) or client secret is used.

**Navigation allowed inside this window:** `https`, port 443, no user-info, and a host equal to or a subdomain of one of `amazon.com`, `amazon.co.uk`, `amazon.ca`, `amazon.de`, `amazon.fr`, `amazon.it`, `amazon.es`, `amazon.co.jp`, `amazon.com.au`, `amazon.in`, `amazon.com.mx`, `amazon.com.br`. Other destinations are blocked ("Amazon requested an unsupported destination."). Pop-up requests open in the same window if allowed.

**Return address check.** Navigation to the return address is intercepted and cancelled. It must be exactly `https://www.amazon.com:443/ap/maplanding` with no user-info and no fragment.

| Return query param | Rule |
|---|---|
| (any) | Duplicate parameter names are rejected |
| `state` | Required. Must equal this attempt's `state`. |
| `error` or `openid.error` | If present: sign-in was cancelled or rejected |
| `openid.oa2.authorization_code` | Required. 1 to 8192 characters, printable ASCII (33 to 126). |
| `openid.oa2.scope` | If present, must be `device_auth_access` |

#### POST https://api.amazon.com/auth/token

Exchanges the Amazon authorization code. Each attempt can be exchanged only once.

| Item | Detail |
|---|---|
| Encoding | JSON (`application/json; charset=utf-8`) |
| Headers | `x-amzn-identity-auth-domain: api.amazon.com`, `Accept: application/json`, `User-Agent: EeroDesktop/1.1.0 (Windows; unofficial)`. No cookies and no `Authorization` header. |
| Redirects | Not followed. A 3xx reply counts as failure. |
| Timeout | 30 s |

| Field | Type | Values / rules |
|---|---|---|
| `app_name` | string | `EeroDesktop` |
| `app_version` | string | `1.1.0` |
| `source_token_type` | string | `authorization_code` |
| `source_token` | string | `openid.oa2.authorization_code` from the return URL |
| `requested_token_type` | string | `access_token` |
| `code_verifier` | string | This attempt's PKCE verifier |
| `code_algorithm` | string | `SHA-256` |
| `client_id` | string | The virtual device client hex, without the `device:` prefix |
| `client_domain` | string | `DeviceLegacy` |

| Response field | Rule |
|---|---|
| (body) | At most 64 KiB, valid JSON |
| `token_type` | Must be `bearer` (case-insensitive) |
| `access_token` | 1 to 8192 printable ASCII characters, with no `;` or `,`. Passed to `POST /2.2/login/amazon` as `auth_token`. |

On a non-2xx reply, the JSON `error` value (if the body is under 64 KiB) is reduced to a known category: `invalid_request`, `invalid_grant`, `invalid_client`, `invalid_token`, `unauthorized_client`, `access_denied`, `invalid_scope`. The service's description text is never shown.

**eero website sign-in**

#### Browser: https://account.eero.com/

Opened in a private embedded browser that uses the general allowlist (see [Embedded browser rules](#embedded-browser-rules)). The user signs in on eero's site. "Use this sign-in" becomes active after a page finishes loading. It reads the browser's cookies for `https://api-user.e2ro.com` and picks the cookie named `s` that is **Secure** and whose domain (ignoring a leading dot) is `api-user.e2ro.com` or `e2ro.com`. That value goes through the token rules and is verified with `GET /2.2/account` before it is saved. If no such cookie exists, the user is told to finish signing in or to use email or phone verification.

**Internet-provider sign-in (eero SSO broker)**

#### GET https://sso.eero.com/api/v1/providers?assetType=mdpi

| Item | Detail |
|---|---|
| Auth | None |
| Redirects | Not followed |
| Timeout | 30 s |
| Size limit | 1 MiB |
| Errors | Any non-2xx status fails |

| Query param | Value |
|---|---|
| `assetType` | `mdpi` |

| Response field | Use |
|---|---|
| Array, or `{ "data": [...] }` | Provider list |
| `[].name` | Name shown in the picker (sorted) |
| `[].idp` | Provider identifier |

Entries without both `name` and `idp` are skipped. An empty list is an error.

#### Browser: GET https://sso.eero.com/?idp={idp}&redirect={redirect}&clientID={client id}

| Query param | Value / rule |
|---|---|
| `idp` | Chosen provider's `idp`. Must match `^[a-zA-Z0-9_-]{1,128}$`. URL-escaped. |
| `redirect` | `https://app.eero.com/auth/index.html` (URL-escaped) |
| `clientID` | A fixed public eero web client identifier (UUID) |

The window uses provider-navigation mode, so the provider's own login pages on any HTTPS host can load.

**Return callback** (intercepted and cancelled):

| Pattern | Rule |
|---|---|
| `https://app.eero.com/auth/index.html` | Exact host, port 443, no user-info, exact path |
| `eero://jumpto/auth` | |

| Callback query param | Rule |
|---|---|
| `token` | Required and non-empty. Duplicate parameters are rejected. |

**Steps after the callback:**

1. `GET /2.2/account` with `Cookie: s=<token>`. This reviews the identity without signing in or saving anything. A rotated `Set-Cookie s=` is carried forward.
2. The user confirms "Continue as {name, email or phone}?".
3. `GET /2.2/account` again with the (possibly rotated) credential, then the session is committed. A later rotation is honored too.

Closing the window cancels verification, and any existing saved session is kept.

### Embedded browser rules

All service windows use a private (in-memory) WebView2 profile with password autosave off.

**General allowlist.** This is used for the starting URL of a service or billing window and for all navigation in windows that are not in provider mode. A URL is allowed only if it is `about:blank`, or HTTPS on port 443 with no user-info whose host equals, or is a subdomain of, one of these domains:

`eero.com`, `e2ro.com`, `amazon.com`, `alexa.com`, `amazon.co.uk`, `amazon.de`, `amazon.fr`, `amazon.it`, `amazon.es`, `amazon.ca`, `amazon.com.au`, `amazon.co.jp`, `amazon.in`, `amazon.com.mx`, `amazon.com.br`, `guardianapp.com`, `1password.com`, `malwarebytes.com`, `stripe.com`, `paypal.com`, `eero.zendesk.com`

**Provider-navigation mode.** This is used by billing windows, internet-provider sign-in and support `CHAT` or `HELP_LINK` contacts. In addition to the allowlist, any `https` URL on port 443 with no user-info may load. `http:`, `file:`, `javascript:`, non-default ports and user-info URLs are always blocked.

**Pop-ups.** New-window requests never open a separate window. They load in the same view if allowed, and are otherwise blocked with a notice.

**Fixed links opened by the app:**

| URL | Feature |
|---|---|
| `https://www.eero.com/support` | eero help articles or support website |
| `https://alexa.com/` | "Alexa": chat with Alexa on the web |
| `https://www.eero.com/legal` | Terms and privacy link during account creation |
| `https://guardianapp.com/terms-and-conditions/` | Guardian terms link during VPN registration |

### Guardian VPN

**eero side**

`{network_id}` is the selected network ID.

#### GET /2.2/partners/guardian/networks/{network_id}/availability

| Response field | Use |
|---|---|
| `seats_available` | Must be a JSON **boolean**. `true` means a VPN device slot is free. `false` means none are free. A missing or non-boolean value means eligibility is not confirmed. |

| Error | Meaning shown |
|---|---|
| 402 | Requires an eligible active benefit (some providers do not include Guardian) |
| 403 | Not enabled for this network, or no permission |
| 404, 405 | Availability not provided for this network or service version |
| Other | Generic message |

Gating: device registration needs `seats_available === true`. Installing the Windows component needs a non-error reply with a boolean `seats_available`.

#### POST /2.2/partners/guardian/networks/{network_id}/pet

Registers this PC as a Guardian VPN device and uses one device slot.

| Item | Detail |
|---|---|
| Encoding | JSON |
| Gating | Availability must report a free slot. The user ticks "I agree to Guardian's terms and privacy policy". No registration is already saved for this account and network. The user confirms. Not available in sample mode. |

| Field | Type | Values / rules | Notes |
|---|---|---|---|
| `nickname` | string | 1 to 100 characters after trimming | Defaults to the Windows computer name |

| Response field | Use |
|---|---|
| `guardian_pet` | Required. Device token used to provision a server (below). Stored encrypted per Windows user. |
| `uuid` | Guardian device ID. Used to recognize this PC in the device list. |
| `expiration` | Stored |

#### POST /2.2/partners/guardian

| Item | Detail |
|---|---|
| Body | None |
| When | "Manage VPN devices", and "Guardian sign-in details" (after the user confirms) |

| Response field | Use |
|---|---|
| `guardian_id` | Guardian subscriber identifier and username (at most 4096 characters when shown) |
| `guardian_secret` | Guardian subscriber secret and password (at most 8192 characters when shown) |

These credentials are held in memory only for the device-management window and are then cleared.

**Guardian API (outside service)**

**Common rules for every Guardian request:**

| Item | Detail |
|---|---|
| Hosts | `connect-api.guardianapp.com` (server API), `wifi-connect-api.guardianapp.com` (partner API), or a VPN gateway host matching `^[a-z0-9][a-z0-9-]{0,62}\.sgw\.guardianapp\.com$` (case-insensitive; lower-cased before use) |
| Scheme and port | HTTPS on 443, no user-info |
| Path | Must start with `/api/v1`. Must not contain `..`, `#`, `\`, whitespace or control characters. |
| Encoding | JSON |
| Cookies | None. No eero cookie is ever sent to Guardian. |
| Redirects | Not followed |
| Timeout | 35 s |
| Compression | gzip and deflate |
| Partner host extras | Header `GRD-Connect-Publishable-Key: <key>`, and the same key added to the JSON body as `connect-publishable-key`. The key is a fixed public routing identifier for eero's Guardian integration, not an account credential. |
| Response | At most 4 MiB. An empty body is treated as `{}`. Any non-2xx status fails with "Guardian could not complete the request (HTTP n)". |

#### GET https://connect-api.guardianapp.com/api/v1.3/servers/all-server-regions/city

Lists VPN locations.

| Response field (array items) | Use |
|---|---|
| `name` | Region ID used in later requests (items without one are skipped) |
| `name-pretty` | Display label (falls back to `name`) |
| `country-iso-code` | Appended to the label, as in "label · CC" |

#### POST https://connect-api.guardianapp.com/api/v1.3/servers/hostnames-for-region

Step 1 of provisioning after a location is chosen.

| Gating | Detail |
|---|---|
| Conditions | The VPN is disconnected, the Windows component is installed, and the PC is registered (a `guardian_pet` is saved) |

| Field | Type | Values / rules |
|---|---|---|
| `region` | string | Chosen region `name` |
| `region-precision` | string | `city` |
| `paid` | boolean | `true` |
| `feature-environment` | integer | `0` |
| `beta-capable` | boolean | `false` |

| Response field (array items) | Use |
|---|---|
| `hostname` | Gateway host. Must match the gateway pattern. |
| `offline` | Entries with `true` are skipped |
| `capacity-score` | The lowest integer score wins. A missing or non-integer score sorts last. |

If no online server is returned, provisioning fails.

#### POST https://connect-api.guardianapp.com/api/v1.2/subscriber-credential/create

Step 2.

| Field | Type | Values / rules |
|---|---|---|
| `validation-method` | string | `pe-token` |
| `pe-token` | string | Saved `guardian_pet` |

| Response field | Use |
|---|---|
| `subscriber-credential` | Required. Used in step 3. |

#### POST https://{gateway}/api/v1.4/device-credentials

Step 3, sent to the selected `*.sgw.guardianapp.com` host. A new WireGuard key pair is generated locally by the installed `wg.exe`. **Only the public key is sent.**

| Field | Type | Values / rules |
|---|---|---|
| `subscriber-credential` | string | From step 2 |
| `transport-protocol` | string | `wireguard` |
| `public-key` | string | Base64 of exactly 32 bytes, not all zero |

| Response field | Rule |
|---|---|
| `server-public-key` | Base64 of 32 bytes, not all zero |
| `mapped-ipv4-address` | IPv4. No prefix or `/32` only. Not loopback, not `0.0.0.0`. |
| `mapped-ipv6-address` | Optional IPv6. No prefix or `/128` only. Not loopback, not `::`. |

**Tunnel configuration built from these values.** It contains no command hooks:

```
[Interface]
PrivateKey = <local private key>
Address = <ipv4>/32[, <ipv6>/128]
DNS = 1.1.1.1, 1.0.0.1

[Peer]
PublicKey = <server-public-key>
AllowedIPs = 0.0.0.0/0, ::/0
Endpoint = <gateway>:51821
PersistentKeepalive = 25
```

The profile is stored encrypted per Windows user, scoped by a hash of the account and the network. When connecting, after Windows administrator approval, the configuration is written machine-encrypted and installed as the Windows service `WireGuardTunnel$Eero-<scope>`. The connection counts as verified only when `wg show` reports a handshake less than 180 s old within 15 one-second checks. Otherwise the tunnel is removed. Only one eero tunnel can run at a time.

#### POST https://wifi-connect-api.guardianapp.com/api/v1.2/partners/subscriber/devices/list

| Field | Type | Values / rules |
|---|---|---|
| `ep-grd-subscriber-identifier` | string | `guardian_id` from `POST /2.2/partners/guardian` (required) |
| `ep-grd-subscriber-secret` | string | `guardian_secret` (required) |
| `connect-publishable-key` | string | Added automatically (partner host) |

| Response field (array items) | Use |
|---|---|
| `ep-grd-device-uuid` | Device ID (items without one are skipped) |
| `ep-grd-device-nickname` | Name (default "VPN device") |
| `ep-grd-device-created-at` | Shown as "Activated" |

#### POST https://wifi-connect-api.guardianapp.com/api/v1.2/partners/subscriber/device/delete

| Gating | Detail |
|---|---|
| Conditions | The user confirms. If the device is this PC, its tunnel must be disconnected first, and the saved local registration is cleared after removal. |

| Field | Type | Values / rules |
|---|---|---|
| `ep-grd-subscriber-identifier` | string | `guardian_id` |
| `ep-grd-subscriber-secret` | string | `guardian_secret` |
| `ep-grd-device-uuid` | string | Device to remove. Must match `^[A-Za-z0-9_-]{1,128}$`. |
| `connect-publishable-key` | string | Added automatically |

**WireGuard component download**

#### GET https://download.wireguard.com/windows-client/wireguard-installer.exe

| Item | Detail |
|---|---|
| Gating | The Guardian availability check returned a boolean `seats_available`, and the user confirmed. Not available in sample mode. |
| Redirects | Not followed. Any non-2xx status fails. |
| Cookies | None |
| Timeout | 2 minutes |
| Size limit | 16 MiB |
| Saved to | A per-user folder readable only by the user and SYSTEM, under a random file name. The file is deleted afterwards. |
| Signature check | Windows Authenticode check (generic verify action, no UI, file-based, revocation not checked, cached URL retrieval only) must succeed, **and** the signing certificate's simple name must be exactly `WireGuard LLC`. |
| Run | Started with administrator elevation. Afterwards `wireguard.exe` and `wg.exe` must exist in `%ProgramW6432%\WireGuard\` (or Program Files). |

Each time the installed `wireguard.exe` or `wg.exe` is used, the same signature check runs again.

## 12. Local device interfaces

### Bluetooth and Windows startup (no HTTP)

- **Bluetooth setup scan.** A passive Bluetooth LE scan for 15 s looks for adverts that carry service UUID `d99a3d3f-c1ef-4533-809d-82de05d509ca`. "Read serial" connects and reads only two standard GATT characteristics: Serial Number String (`0x2A25`) and Firmware Revision String (`0x2A26`). Nothing is written to the eero. The advertised name `<version>-<a>-<b>` is decoded as follows: on version 3, `a == "1"` means already in a network; on version 4 or later, `b == "1"` means Ethernet is connected.
- **Windows startup.** "Start with Windows" writes the per-user Run entry `EeroDesktop` = `"<exe path>" --background`.

## 13. Known limits of this reference

- **Responses shown as-is.** Several "View" actions display eero's whole response without reading particular fields. For those endpoints only the request is documented.
- **Alternative response keys.** Some lists are read from whichever of several keys eero returns, for example a top-level array, `notifications` or `items` for notification history, or `subnets` or `configurations` for additional wifi networks. Every key the app accepts is listed, because eero's exact response shape isn't fixed.
- **Subscription details.** `GET /2.2/premium/customer` is read as `plan`, then `plan_name`, then `name`, whichever is present first.
- **Chat.** The message body format accepts a `ui_event` object, but eero Desktop never sends one, so its shape isn't documented. A new chat `mcp_token` is requested for every message (with an empty `refresh_token`), so how long a token lasts on eero's side isn't known.
- **Partner offers.** Steps 1 and 3 send `authentication_session_id`, while step 2 sends `session_id`. That is how the app builds the requests.
- **Identifiers not checked.** The Multiple static IPs load (`/2.3/networks/{id}/multistaticip`) and eero firmware history (`/2.2/eeros/{id}/firmware_update/history`) use the last segment of the URL without the identifier check described in Conventions.

## 14. Endpoint index

Every endpoint in this reference, in the order it appears. Paths are shown as written in their section, with `{network_url}` and resource names where eero supplies the URL.

| Endpoint | Section |
|---|---|
| [`GET /2.2/account (connection check)`](#get-22account-connection-check) | Sign-in and registration |
| [`POST /2.2/login`](#post-22login) | Sign-in and registration |
| [`POST /2.2/login/verify`](#post-22loginverify) | Sign-in and registration |
| [`POST /2.2/login/resend`](#post-22loginresend) | Sign-in and registration |
| [`POST /2.2/login/refresh`](#post-22loginrefresh) | Sign-in and registration |
| [`POST /2.2/login/amazon`](#post-22loginamazon) | Sign-in and registration |
| [`GET /2.2/consents/account?country={country}`](#get-22consentsaccountcountrycountry) | Sign-in and registration |
| [`POST /2.2/register`](#post-22register) | Sign-in and registration |
| [`POST /2.2/register/verify`](#post-22registerverify) | Sign-in and registration |
| [`POST /2.2/register/phone/resend`](#post-22registerphoneresend) | Sign-in and registration |
| [`POST /2.2/logout`](#post-22logout) | Sign-in and registration |
| [`GET /2.2/consents/account`](#get-22consentsaccount) | Account creation |
| [`GET /2.2/account`](#get-22account) | Account, dashboard and notifications |
| [`GET /2.2/networks`](#get-22networks) | Account, dashboard and notifications |
| [`GET {network url}`](#get-network-url) | Account, dashboard and notifications |
| [`GET {resources.devices} (default {network url}/devices)`](#get-resourcesdevices-default-network-urldevices) | Account, dashboard and notifications |
| [`GET {resources.eeros} (default {network url}/eeros)`](#get-resourceseeros-default-network-urleeros) | Account, dashboard and notifications |
| [`GET {resources.profiles} (default {network url}/profiles)`](#get-resourcesprofiles-default-network-urlprofiles) | Account, dashboard and notifications |
| [`POST {resources.speedtest} (default {network url}/speedtest)`](#post-resourcesspeedtest-default-network-urlspeedtest) | Account, dashboard and notifications |
| [`GET {network url}/notifications_history?limit=50`](#get-network-urlnotifications_historylimit50) | Account, dashboard and notifications |
| [`GET /2.2/account (Account settings)`](#get-22account-account-settings) | Settings › Account settings |
| [`PUT /2.2/account/name`](#put-22accountname) | Settings › Account settings |
| [`PUT /2.2/account/email`](#put-22accountemail) | Settings › Account settings |
| [`POST /2.2/account/email/verify`](#post-22accountemailverify) | Settings › Account settings |
| [`PUT /2.2/account/phone`](#put-22accountphone) | Settings › Account settings |
| [`POST /2.2/account/phone/verify`](#post-22accountphoneverify) | Settings › Account settings |
| [`PUT /2.2/account/consents`](#put-22accountconsents) | Settings › Account settings |
| [`DELETE /2.2/account`](#delete-22account) | Settings › Account settings |
| [`POST /2.2/register/email/resend`](#post-22registeremailresend) | Settings › Account settings › Account data |
| [`GET /2.2/data-controller/info`](#get-22data-controllerinfo) | Settings › Account settings › Account data |
| [`GET /2.2/countries/sms`](#get-22countriessms) | Settings › Account settings › Account data |
| [`GET /2.2/app_configuration[?networkId={network_id}]`](#get-22app_configurationnetworkidnetwork_id) | Settings › Account settings › Account data |
| [`GET /2.2/directedid?d=0&i=0&p=0`](#get-22directedidd0i0p0) | Settings › Account settings › Account data |
| [`GET /2.2/account/trust`](#get-22accounttrust) | Settings › Account settings › Trusted certificates |
| [`POST /2.2/account/trust`](#post-22accounttrust) | Settings › Account settings › Trusted certificates |
| [`PUT /2.2/account/trust`](#put-22accounttrust) | Settings › Account settings › Trusted certificates |
| [`DELETE /2.2/account/trust/{certificate_serial}`](#delete-22accounttrustcertificate_serial) | Settings › Account settings › Trusted certificates |
| [`Time-range query format (start / end)`](#time-range-query-format-start--end) | Endpoints loaded when a page opens |
| [`Chart responses (Data usage, Security activity, per-device and per-eero usage)`](#chart-responses-data-usage-security-activity-per-device-and-per-eero-usage) | Endpoints loaded when a page opens |
| [`GET {network_url}`](#get-network_url) | Network refresh after a change |
| [`GET resource devices (fallback {network_url}/devices)`](#get-resource-devices-fallback-network_urldevices) | Network refresh after a change |
| [`GET resource eeros (fallback {network_url}/eeros)`](#get-resource-eeros-fallback-network_urleeros) | Network refresh after a change |
| [`GET resource profiles (fallback {network_url}/profiles)`](#get-resource-profiles-fallback-network_urlprofiles) | Network refresh after a change |
| [`POST resource speedtest (fallback {network_url}/speedtest)`](#post-resource-speedtest-fallback-network_urlspeedtest) | Home › Internet |
| [`GET resource speedtest (fallback {network_url}/speedtest)`](#get-resource-speedtest-fallback-network_urlspeedtest) | Home › Internet |
| [`POST resource profiles (fallback {network_url}/profiles)`](#post-resource-profiles-fallback-network_urlprofiles) | Home › Profiles |
| [`GET {profile_url}`](#get-profile_url) | Home › Profiles |
| [`PUT {profile_url}: pause / resume`](#put-profile_url-pause--resume) | Home › Profiles |
| [`PUT {profile_url}: rename`](#put-profile_url-rename) | Home › Profiles |
| [`PUT {profile_url}: assign devices`](#put-profile_url-assign-devices) | Home › Profiles |
| [`DELETE {profile_url}`](#delete-profile_url) | Home › Profiles |
| [`POST resource schedules of the profile (fallback {profile_url}/schedules): add scheduled pause`](#post-resource-schedules-of-the-profile-fallback-profile_urlschedules-add-scheduled-pause) | Home › Profiles |
| [`PUT {schedule_url}: edit scheduled pause`](#put-schedule_url-edit-scheduled-pause) | Home › Profiles |
| [`DELETE {schedule_url}`](#delete-schedule_url) | Home › Profiles |
| [`GET {network_url}/dns_policies/profiles/{profile_id}`](#get-network_urldns_policiesprofilesprofile_id) | Home › Profiles |
| [`POST {network_url}/dns_policies/profiles/{profile_id}`](#post-network_urldns_policiesprofilesprofile_id) | Home › Profiles |
| [`POST {network_url}/dns_policies/profiles/{profile_id}/adblock`](#post-network_urldns_policiesprofilesprofile_idadblock) | Home › Profiles |
| [`GET {network_url}/dns_policies/profiles/{profile_id}/applications`](#get-network_urldns_policiesprofilesprofile_idapplications) | Home › Profiles |
| [`PUT {network_url}/dns_policies/profiles/{profile_id}/applications/blocked`](#put-network_urldns_policiesprofilesprofile_idapplicationsblocked) | Home › Profiles |
| [`POST /2.2/networks/{network_id}/profiles/{profile_id}/schedules`](#post-22networksnetwork_idprofilesprofile_idschedules) | Home › Profiles › Scheduled pauses |
| [`PUT {pause_schedule_url}`](#put-pause_schedule_url) | Home › Profiles › Scheduled pauses |
| [`GET {network_url}/dns_policies/network`](#get-network_urldns_policiesnetwork) | Home › Security & privacy |
| [`POST {network_url}/dns_policies/network`](#post-network_urldns_policiesnetwork) | Home › Security & privacy |
| [`POST {network_url}/dns_policies/adblock`](#post-network_urldns_policiesadblock) | Home › Security & privacy |
| [`GET {network_url}/dns_policies/advanced_content_filter`](#get-network_urldns_policiesadvanced_content_filter) | Home › Security & privacy |
| [`PUT {network_url}/dns_policies/network/blocked`](#put-network_urldns_policiesnetworkblocked) | Home › Security & privacy |
| [`PUT {network_url}/dns_policies/network/allowed`](#put-network_urldns_policiesnetworkallowed) | Home › Security & privacy |
| [`PUT {network_url}/dns_policies/profiles/blocked`](#put-network_urldns_policiesprofilesblocked) | Home › Security & privacy |
| [`PUT {network_url}/dns_policies/profiles/allowed`](#put-network_urldns_policiesprofilesallowed) | Home › Security & privacy |
| [`GET /2.2/networks/{network_id}/safe_filter_levels?safeFilterType={type}`](#get-22networksnetwork_idsafe_filter_levelssafefiltertypetype) | Home › Security & privacy › Advanced |
| [`PUT /2.2/networks/{network_id}/dns_policies/network/allowed/cnames`](#put-22networksnetwork_iddns_policiesnetworkallowedcnames) | Home › Security & privacy › Advanced |
| [`PUT /2.2/networks/{network_id}/dns_policies/profiles/allowed/cnames`](#put-22networksnetwork_iddns_policiesprofilesallowedcnames) | Home › Security & privacy › Advanced |
| [`GET /2.2/account/identity_protection`](#get-22accountidentity_protection) | Home › Security & privacy › Identity protection |
| [`GET /2.2/networks/{network_id}/identity_protection/countries`](#get-22networksnetwork_ididentity_protectioncountries) | Home › Security & privacy › Identity protection |
| [`POST /2.2/account/identity_protection/signup`](#post-22accountidentity_protectionsignup) | Home › Security & privacy › Identity protection |
| [`POST /2.2/account/identity_protection/scan`](#post-22accountidentity_protectionscan) | Home › Security & privacy › Identity protection |
| [`POST /2.2/account/identity_protection/scan/email`](#post-22accountidentity_protectionscanemail) | Home › Security & privacy › Identity protection |
| [`POST /2.2/account/identity_protection/verify_email`](#post-22accountidentity_protectionverify_email) | Home › Security & privacy › Identity protection |
| [`GET {eero_url}`](#get-eero_url) | Home › eeros › Details |
| [`PUT resource led_action of the eero`](#put-resource-led_action-of-the-eero) | Home › eeros › Details |
| [`PUT {eero_url}/nightlight/settings`](#put-eero_urlnightlightsettings) | Home › eeros › Details |
| [`GET {eero_connections_url}`](#get-eero_connections_url) | Home › eeros › Details |
| [`POST {eero_url}/ports/{interface_number}/action`](#post-eero_urlportsinterface_numberaction) | Home › eeros › Details |
| [`POST {eero_url}/action`](#post-eero_urlaction) | Home › eeros › Details |
| [`GET /2.2/eeros/{eero_id}/firmware_update/history`](#get-22eeroseero_idfirmware_updatehistory) | Home › eeros › Details |
| [`GET /2.2/networks/{network_id}/data_usage/eeros/{eero_id}`](#get-22networksnetwork_iddata_usageeeroseero_id) | Home › eeros › Details |
| [`POST /2.2/eeros/{serial}/led_cycle`](#post-22eerosserialled_cycle) | Home › eeros › Details |
| [`POST resource reboot of the eero (fallback {eero_url}/reboot)`](#post-resource-reboot-of-the-eero-fallback-eero_urlreboot) | Home › eeros › Details |
| [`DELETE {eero_url}`](#delete-eero_url) | Home › eeros › Details |
| [`POST /2.2/eeros/{eero_id}/nightlight/override`](#post-22eeroseero_idnightlightoverride) | Home › eeros › Details (more actions) |
| [`GET /2.2/eeros/{eero_id}/authorization_status?network_id={network_id}`](#get-22eeroseero_idauthorization_statusnetwork_idnetwork_id) | Home › eeros › Details (more actions) |
| [`GET /2.2/eeros/{serial}/license_expiration?network={network_id}`](#get-22eerosseriallicense_expirationnetworknetwork_id) | Home › eeros › Details (more actions) |
| [`GET /2.2/eeros/{serial}/pre_setup_config`](#get-22eerosserialpre_setup_config) | Home › eeros › Details (more actions) |
| [`POST /2.3/networks/{network_id}/placement_rating/{serial}`](#post-23networksnetwork_idplacement_ratingserial) | Home › eeros › Details (more actions) |
| [`POST /2.3/networks/{network_id}/placement_test/{serial}`](#post-23networksnetwork_idplacement_testserial) | Home › eeros › Placement tests |
| [`GET /2.3/networks/{network_id}/placement_test/{serial}`](#get-23networksnetwork_idplacement_testserial) | Home › eeros › Placement tests |
| [`POST /2.2/eeros?requireSession=true`](#post-22eerosrequiresessiontrue) | Home › Add or replace eero |
| [`GET {network_url}/auto_discovered_eeros`](#get-network_urlauto_discovered_eeros) | Home › Add or replace eero |
| [`PUT {network_url}/auto_setup_mode`](#put-network_urlauto_setup_mode) | Home › Add or replace eero |
| [`GET /2.3/networks/{network_id}/pending_eeros`](#get-23networksnetwork_idpending_eeros) | Home › Add or replace eero › Setup tools |
| [`GET /2.2/networks/{network_id}/packaged_eeros`](#get-22networksnetwork_idpackaged_eeros) | Home › Add or replace eero › Setup tools |
| [`GET /2.2/eero_models/capabilities?networkId={network_id}`](#get-22eero_modelscapabilitiesnetworkidnetwork_id) | Home › Add or replace eero › Setup tools |
| [`GET /2.2/networks/{network_id}/ouicheck?serial={serial}[&version={version}]`](#get-22networksnetwork_idouicheckserialserialversionversion) | Home › Add or replace eero › Setup tools |
| [`POST /2.2/networks/{network_id}/node_setup`](#post-22networksnetwork_idnode_setup) | Home › Add or replace eero › Setup tools |
| [`POST /2.2/eeros/networks/{network_id}/notify_swap`](#post-22eerosnetworksnetwork_idnotify_swap) | Home › Add or replace eero › Setup tools |
| [`GET /2.2/consents/network?serial={serial}`](#get-22consentsnetworkserialserial) | Home › Add or replace eero › Create a network |
| [`POST /2.3/networks`](#post-23networks) | Home › Add or replace eero › Create a network |
| [`POST /2.2/eeros?requireSession=true (Create a network)`](#post-22eerosrequiresessiontrue-create-a-network) | Home › Add or replace eero › Create a network |
| [`PUT {device_url}: pause / resume`](#put-device_url-pause--resume) | Devices › Device details and actions |
| [`PUT {device_url}: nickname`](#put-device_url-nickname) | Devices › Device details and actions |
| [`PUT {device_url}: profile`](#put-device_url-profile) | Devices › Device details and actions |
| [`PUT {network_url}/devices/{device_mac}: device type`](#put-network_urldevicesdevice_mac-device-type) | Devices › Device details and actions |
| [`PUT /2.3/networks/{network_id}/devices/{device_mac}: internet backup access`](#put-23networksnetwork_iddevicesdevice_mac-internet-backup-access) | Devices › Device details and actions |
| [`GET {network_url}/data_usage/devices/{device_mac}`](#get-network_urldata_usagedevicesdevice_mac) | Devices › Device details and actions |
| [`POST {network_url}/blacklist: block`](#post-network_urlblacklist-block) | Devices › Device details and actions |
| [`DELETE {network_url}/blacklist/{device_mac}: unblock`](#delete-network_urlblacklistdevice_mac-unblock) | Devices › Device details and actions |
| [`GET {network_url}/blacklist`](#get-network_urlblacklist) | Devices › Device details and actions |
| [`GET /2.2/networks/{network_id}/blacklist`](#get-22networksnetwork_idblacklist) | Devices › Blocked devices |
| [`DELETE /2.2/networks/{network_id}/blacklist/{device_mac}`](#delete-22networksnetwork_idblacklistdevice_mac) | Devices › Blocked devices |
| [`GET /2.2/networks/{network_id}/speedtest`](#get-22networksnetwork_idspeedtest) | Activity › Internet (speed) |
| [`POST /2.2/networks/{network_id}/speedtest`](#post-22networksnetwork_idspeedtest) | Activity › Internet (speed) |
| [`GET /2.2/networks/{network_id}/data_usage`](#get-22networksnetwork_iddata_usage) | Activity › Data usage |
| [`GET /2.2/networks/{network_id}/data_usage/breakdown`](#get-22networksnetwork_iddata_usagebreakdown) | Activity › Data usage |
| [`GET /2.2/networks/{network_id}/data_usage/report_settings`](#get-22networksnetwork_iddata_usagereport_settings) | Activity › Data usage |
| [`PUT /2.2/networks/{network_id}/data_usage/report_settings`](#put-22networksnetwork_iddata_usagereport_settings) | Activity › Data usage |
| [`GET /2.2/networks/{network_id}/app_events`](#get-22networksnetwork_idapp_events) | Activity › Network events |
| [`GET /2.2/networks/{network_id}/insights`](#get-22networksnetwork_idinsights) | Activity › Security activity |
| [`GET /2.2/networks/{network_id}/insights/devices`](#get-22networksnetwork_idinsightsdevices) | Activity › Security activity |
| [`GET /2.2/networks/{network_id}/insights/profiles`](#get-22networksnetwork_idinsightsprofiles) | Activity › Security activity |
| [`GET /2.2/networks/{network_id}/data_usage/eeros/summary?start={start}&end={end}&cadence=daily`](#get-22networksnetwork_iddata_usageeerossummarystartstartendendcadencedaily) | Activity › Usage details |
| [`GET /2.2/networks/{network_id}/data_usage/eeros/{eero_id}?start={start}&end={end}&cadence=daily`](#get-22networksnetwork_iddata_usageeeroseero_idstartstartendendcadencedaily) | Activity › Usage details |
| [`GET /2.2/networks/{network_id}/data_usage/profiles/{profile_id}?start={start}&end={end}&cadence=daily`](#get-22networksnetwork_iddata_usageprofilesprofile_idstartstartendendcadencedaily) | Activity › Usage details |
| [`GET /2.2/networks/{network_id}/data_usage/unprofiled/devices?start={start}&end={end}`](#get-22networksnetwork_iddata_usageunprofileddevicesstartstartendend) | Activity › Usage details |
| [`GET /2.2/networks/{network_id}/data_usage/unprofiled/summary?start={start}&end={end}&cadence=daily`](#get-22networksnetwork_iddata_usageunprofiledsummarystartstartendendcadencedaily) | Activity › Usage details |
| [`GET /2.2/networks/{network_id}/insights/devices/{device_mac}?start={start}&end={end}&cadence=daily`](#get-22networksnetwork_idinsightsdevicesdevice_macstartstartendendcadencedaily) | Activity › Usage details |
| [`GET /2.2/networks/{network_id}/insights/profiles/{profile_id}?start={start}&end={end}&cadence=daily`](#get-22networksnetwork_idinsightsprofilesprofile_idstartstartendendcadencedaily) | Activity › Usage details |
| [`GET /2.2/networks/{network_id}/insights/profiles/{profile_id}/devices?start={start}&end={end}`](#get-22networksnetwork_idinsightsprofilesprofile_iddevicesstartstartendend) | Activity › Usage details |
| [`PUT {network_url}`](#put-network_url) | Settings › Wifi name & password |
| [`GET /2.2/networks/{network_id}/password`](#get-22networksnetwork_idpassword) | Settings › Wifi name & password |
| [`PUT /2.2/networks/{network_id}/password`](#put-22networksnetwork_idpassword) | Settings › Wifi name & password |
| [`POST /2.2/util/generatepassword`](#post-22utilgeneratepassword) | Settings › Wifi name & password |
| [`GET /2.2/networks/{network_id}/guestnetwork`](#get-22networksnetwork_idguestnetwork) | Settings › Guest wifi network |
| [`PUT /2.2/networks/{network_id}/guestnetwork`](#put-22networksnetwork_idguestnetwork) | Settings › Guest wifi network |
| [`GET /2.2/networks/{network_id}/guestnetwork/password`](#get-22networksnetwork_idguestnetworkpassword) | Settings › Guest wifi network |
| [`PUT /2.2/networks/{network_id}/guestnetwork/password`](#put-22networksnetwork_idguestnetworkpassword) | Settings › Guest wifi network |
| [`PUT /2.2/networks/{network_id}/label?labelType=specialMarket`](#put-22networksnetwork_idlabellabeltypespecialmarket) | Settings › Nickname & location |
| [`PUT /2.2/networks/{network_id}/settings`](#put-22networksnetwork_idsettings) | Settings › Network settings (shared write) |
| [`GET /2.2/networks/{network_id}/members`](#get-22networksnetwork_idmembers) | Settings › User permissions |
| [`GET /2.2/networks/{network_id}/invites`](#get-22networksnetwork_idinvites) | Settings › User permissions |
| [`POST /2.2/networks/{network_id}/member_promotion`](#post-22networksnetwork_idmember_promotion) | Settings › User permissions |
| [`DELETE /2.2/networks/{network_id}/admins/{user_id}`](#delete-22networksnetwork_idadminsuser_id) | Settings › User permissions |
| [`POST /2.2/networks/{network_id}/invites`](#post-22networksnetwork_idinvites) | Settings › User permissions |
| [`PUT /2.2/networks/{network_id}/invites/{invite_id}`](#put-22networksnetwork_idinvitesinvite_id) | Settings › User permissions |
| [`DELETE /2.2/networks/{network_id}/invites/{invite_id}`](#delete-22networksnetwork_idinvitesinvite_id) | Settings › User permissions |
| [`POST /2.2/networks/{network_id}/invites/cancel_pending_admin`](#post-22networksnetwork_idinvitescancel_pending_admin) | Settings › User permissions |
| [`POST /2.2/inviteQuery`](#post-22invitequery) | Settings › User permissions › Join a network |
| [`POST /2.2/networks/{network_id}/invites/response`](#post-22networksnetwork_idinvitesresponse) | Settings › User permissions › Join a network |
| [`GET /2.2/incoming_member_promotions`](#get-22incoming_member_promotions) | Settings › User permissions › Incoming access invitations |
| [`POST /2.2/networks/{network_id}/invites/response (Incoming access invitations)`](#post-22networksnetwork_idinvitesresponse-incoming-access-invitations) | Settings › User permissions › Incoming access invitations |
| [`GET /2.2/networks/{network_id}/updates`](#get-22networksnetwork_idupdates) | Settings › Software updates |
| [`POST /2.2/networks/{network_id}/updates`](#post-22networksnetwork_idupdates) | Settings › Software updates |
| [`POST /2.2/networks/{network_id}/updates/preferred_update_hour`](#post-22networksnetwork_idupdatespreferred_update_hour) | Settings › Software updates |
| [`GET /2.2/networks/{network_id}/notifications`](#get-22networksnetwork_idnotifications) | Settings › Notifications |
| [`PUT /2.2/networks/{network_id}/notifications`](#put-22networksnetwork_idnotifications) | Settings › Notifications |
| [`PUT /2.2/account/push_settings`](#put-22accountpush_settings) | Settings › Notifications |
| [`GET /2.2/networks/{network_id}/notifications_history`](#get-22networksnetwork_idnotifications_history) | Settings › Notifications |
| [`GET /2.2/networks/{network_id}/notifications/has_unread`](#get-22networksnetwork_idnotificationshas_unread) | Settings › Notifications |
| [`POST /2.2/networks/{network_id}/notifications/mark_read`](#post-22networksnetwork_idnotificationsmark_read) | Settings › Notifications |
| [`GET {network_url}/notifications_history`](#get-network_urlnotifications_history) | Settings › Notifications › Windows notifications |
| [`POST /2.2/networks/{network_id}/diagnostics`](#post-22networksnetwork_iddiagnostics) | Settings › Troubleshooting |
| [`GET /2.2/networks/{network_id}/diagnostics`](#get-22networksnetwork_iddiagnostics) | Settings › Troubleshooting |
| [`GET /2.2/networks/{network_id}/network_scan`](#get-22networksnetwork_idnetwork_scan) | Settings › Troubleshooting |
| [`GET /2.4/networks/{network_id}/placement/wifi_coverage_scan`](#get-24networksnetwork_idplacementwifi_coverage_scan) | Settings › Troubleshooting |
| [`GET /2.4/networks/{network_id}/placement/wifi_generation_scan`](#get-24networksnetwork_idplacementwifi_generation_scan) | Settings › Troubleshooting |
| [`POST resource diagnostics (fallback {network_url}/diagnostics)`](#post-resource-diagnostics-fallback-network_urldiagnostics) | Settings › Troubleshooting › Guided diagnostics |
| [`GET resource diagnostics (fallback {network_url}/diagnostics)`](#get-resource-diagnostics-fallback-network_urldiagnostics) | Settings › Troubleshooting › Guided diagnostics |
| [`PUT {network_url}/temporary_flags/hide_5g`](#put-network_urltemporary_flagshide_5g) | Settings › Troubleshooting › Connect a device |
| [`DELETE {network_url}/temporary_flags/hide_5g`](#delete-network_urltemporary_flagshide_5g) | Settings › Troubleshooting › Connect a device |
| [`GET /2.2/premium/customer`](#get-22premiumcustomer) | Settings › Account settings › Subscriptions |
| [`POST /2.2/license_keys (validate)`](#post-22license_keys-validate) | Settings › Account settings › Subscriptions |
| [`POST /2.2/license_keys (activate)`](#post-22license_keys-activate) | Settings › Account settings › Subscriptions |
| [`GET /2.2/account/1password_status`](#get-22account1password_status) | Settings › Account settings › Subscriptions |
| [`GET /2.2/partners/onepassword/coupon`](#get-22partnersonepasswordcoupon) | Settings › Account settings › Subscriptions |
| [`GET /2.2/partners/malwarebytes/coupon`](#get-22partnersmalwarebytescoupon) | Settings › Account settings › Subscriptions |
| [`GET /2.4/premium/plans/{network_id}`](#get-24premiumplansnetwork_id) | Settings › Account settings › Subscriptions › Offers & referrals |
| [`POST /2.2/premium/in_app_payments/offers/{network_id}`](#post-22premiumin_app_paymentsoffersnetwork_id) | Settings › Account settings › Subscriptions › Offers & referrals |
| [`PUT /2.2/subscription_referrals`](#put-22subscription_referrals) | Settings › Account settings › Subscriptions › Offers & referrals |
| [`POST /2.2/subscription_referral/email`](#post-22subscription_referralemail) | Settings › Account settings › Subscriptions › Offers & referrals |
| [`GET {network_url}/managed/subscription_referrals`](#get-network_urlmanagedsubscription_referrals) | Settings › Account settings › Subscriptions › Offers & referrals |
| [`GET /2.2/login/temporary`](#get-22logintemporary) | Settings › Account settings › Subscriptions › Offers & referrals |
| [`GET /2.2/entitlements/networks/{network_id}/features`](#get-22entitlementsnetworksnetwork_idfeatures) | Settings › Account settings › Subscriptions › Entitlements & trials |
| [`GET /2.2/entitlements/networks/{network_id}/upsell_features`](#get-22entitlementsnetworksnetwork_idupsell_features) | Settings › Account settings › Subscriptions › Entitlements & trials |
| [`POST /2.2/premium/customer/plans`](#post-22premiumcustomerplans) | Settings › Account settings › Subscriptions › Entitlements & trials |
| [`GET /2.2/partners/upsells/offers[?opt_network_id={network_id}]`](#get-22partnersupsellsoffersopt_network_idnetwork_id) | Settings › Account settings › Subscriptions › Partner offers |
| [`GET /2.2/partners/upsells/authentication_methods`](#get-22partnersupsellsauthentication_methods) | Settings › Account settings › Subscriptions › Partner offers |
| [`POST /2.2/partners/upsells/authentication_methods`](#post-22partnersupsellsauthentication_methods) | Settings › Account settings › Subscriptions › Partner offers |
| [`PUT /2.2/partners/upsells/authentication`](#put-22partnersupsellsauthentication) | Settings › Account settings › Subscriptions › Partner offers |
| [`POST /2.2/partners/upsells/orders`](#post-22partnersupsellsorders) | Settings › Account settings › Subscriptions › Partner offers |
| [`POST /2.2/partners/encryptme`](#post-22partnersencryptme) | Settings › Account settings › Subscriptions › Partner offers |
| [`GET /2.2/networks/{network_id}/proxied_nodes`](#get-22networksnetwork_idproxied_nodes) | Settings › Connected services |
| [`PUT /2.2/networks/{network_id}/proxied_nodes`](#put-22networksnetwork_idproxied_nodes) | Settings › Connected services |
| [`GET /2.2/networks/{network_id}/simple_setup/vendors`](#get-22networksnetwork_idsimple_setupvendors) | Settings › Connected services |
| [`PUT /2.2/networks/{network_id}/simple_setup/vendors/{vendor_id}`](#put-22networksnetwork_idsimple_setupvendorsvendor_id) | Settings › Connected services |
| [`POST /2.2/amazon/networks/{network_id}/account_unlink`](#post-22amazonnetworksnetwork_idaccount_unlink) | Settings › Connected services › Amazon connected home |
| [`POST /2.3/networks/{network_id}/shh/start_zigbee_discovery`](#post-23networksnetwork_idshhstart_zigbee_discovery) | Settings › Connected services › Amazon connected home |
| [`GET {network_url}/accessories/setup/status`](#get-network_urlaccessoriessetupstatus) | Settings › Connected services › Accessories & cellular |
| [`GET {network_url}/accessories/{serial}`](#get-network_urlaccessoriesserial) | Settings › Connected services › Accessories & cellular |
| [`GET {network_url}/sip_config_associations/{eero_id}`](#get-network_urlsip_config_associationseero_id) | Settings › Connected services › Accessories & cellular |
| [`DELETE {network_url}/accessories/{serial}`](#delete-network_urlaccessoriesserial) | Settings › Connected services › Accessories & cellular |
| [`POST /2.2/networks/{network_id}/transfer (send invitation)`](#post-22networksnetwork_idtransfer-send-invitation) | Settings › Network management |
| [`GET /2.2/transfer/has_networks`](#get-22transferhas_networks) | Settings › Network management |
| [`GET /2.2/networks/{network_id}/transfer`](#get-22networksnetwork_idtransfer) | Settings › Network management |
| [`POST /2.2/networks/{network_id}/transfer (resend)`](#post-22networksnetwork_idtransfer-resend) | Settings › Network management |
| [`DELETE /2.2/networks/{network_id}/transfer`](#delete-22networksnetwork_idtransfer) | Settings › Network management |
| [`GET /2.2/network_transfers/pending`](#get-22network_transferspending) | Settings › Network management |
| [`POST /2.2/network_transfers/{transfer_id}/accept`](#post-22network_transferstransfer_idaccept) | Settings › Network management |
| [`POST /2.2/network_transfers/{transfer_id}/deny`](#post-22network_transferstransfer_iddeny) | Settings › Network management |
| [`DELETE /2.2/networks/{network_id}`](#delete-22networksnetwork_id) | Settings › Network management |
| [`GET /2.2/transfer/has_networks?email={email} \| ?phone={phone}`](#get-22transferhas_networksemailemail--phonephone) | Settings › Network management › Check recipient |
| [`PUT /2.2/networks/{network_id}/network_customer_type`](#put-22networksnetwork_idnetwork_customer_type) | Settings › Network management › Organization & community |
| [`GET /2.2/networks/{network_id}/community`](#get-22networksnetwork_idcommunity) | Settings › Network management › Organization & community |
| [`GET /2.2/networks/{network_id}/cobranding`](#get-22networksnetwork_idcobranding) | Settings › Network management › Organization & community |
| [`GET /2.2/managed_summary`](#get-22managed_summary) | Settings › Network management › Organization & community |
| [`GET /2.2/organizations/self/networks/pro_managed[?network_name_filter={text}]`](#get-22organizationsselfnetworkspro_managednetwork_name_filtertext) | Settings › Network management › Organization & community |
| [`GET /2.2/organizations/self/subsets?limit=50[&search={text}]`](#get-22organizationsselfsubsetslimit50searchtext) | Settings › Network management › Organization & community |
| [`PUT /2.2/organizations/self/subsets/{subset_id}/associate`](#put-22organizationsselfsubsetssubset_idassociate) | Settings › Network management › Organization & community |
| [`POST /2.2/networks/{network_id}/holding_state`](#post-22networksnetwork_idholding_state) | Settings › Network management › Organization & community |
| [`GET /2.2/networks/{network_id}/ac_compat`](#get-22networksnetwork_idac_compat) | Settings › Advanced networking › Wireless settings |
| [`PUT /2.2/networks/{network_id}/ac_compat`](#put-22networksnetwork_idac_compat) | Settings › Advanced networking › Wireless settings |
| [`PUT /2.2/networks/{network_id}/mlo_mode`](#put-22networksnetwork_idmlo_mode) | Settings › Advanced networking › Wireless settings |
| [`PUT /2.2/networks/{network_id}/passpoint/enabled`](#put-22networksnetwork_idpasspointenabled) | Settings › Advanced networking › Wireless settings |
| [`GET /2.2/networks/{network_id}/radio_settings`](#get-22networksnetwork_idradio_settings) | Settings › Advanced networking › Wireless settings |
| [`GET /2.2/networks/{network_id}/channel_utilization?start={start}&end={end}`](#get-22networksnetwork_idchannel_utilizationstartstartendend) | Settings › Advanced networking › Wireless settings |
| [`GET /2.2/networks/{network_id}/wpa3_per_band`](#get-22networksnetwork_idwpa3_per_band) | Settings › Advanced networking › Wireless settings › Security by radio band |
| [`PUT /2.2/networks/{network_id}/wpa3_per_band`](#put-22networksnetwork_idwpa3_per_band) | Settings › Advanced networking › Wireless settings › Security by radio band |
| [`GET {network_url}/fast_transition`](#get-network_urlfast_transition) | Settings › Advanced networking › Wireless settings › Fast roaming |
| [`PUT {network_url}/fast_transition`](#put-network_urlfast_transition) | Settings › Advanced networking › Wireless settings › Fast roaming |
| [`GET /2.2/networks/{network_id}/thread`](#get-22networksnetwork_idthread) | Settings › Advanced networking › Thread |
| [`PUT /2.2/networks/{network_id}/thread`](#put-22networksnetwork_idthread) | Settings › Advanced networking › Thread |
| [`POST /2.2/networks/{network_id}/thread`](#post-22networksnetwork_idthread) | Settings › Advanced networking › Thread |
| [`PUT /2.2/networks/{network_id}/ddns/enable`](#put-22networksnetwork_idddnsenable) | Settings › Advanced networking › Dynamic DNS |
| [`PUT /2.2/networks/{network_id}/ddns/disable`](#put-22networksnetwork_idddnsdisable) | Settings › Advanced networking › Dynamic DNS |
| [`GET /2.2/networks/{network_id}/routing`](#get-22networksnetwork_idrouting) | Settings › Advanced networking › Reservations & port forwarding |
| [`POST /2.2/networks/{network_id}/reservations`](#post-22networksnetwork_idreservations) | Settings › Advanced networking › Reservations & port forwarding |
| [`PUT {reservation_url}`](#put-reservation_url) | Settings › Advanced networking › Reservations & port forwarding |
| [`DELETE {reservation_url}?delete_forwards=true`](#delete-reservation_urldelete_forwardstrue) | Settings › Advanced networking › Reservations & port forwarding |
| [`POST /2.2/networks/{network_id}/forwards`](#post-22networksnetwork_idforwards) | Settings › Advanced networking › Reservations & port forwarding |
| [`PUT {forward_url}`](#put-forward_url) | Settings › Advanced networking › Reservations & port forwarding |
| [`DELETE {forward_url}`](#delete-forward_url) | Settings › Advanced networking › Reservations & port forwarding |
| [`POST /2.2/networks/{network_id}/ipv6/pinholes`](#post-22networksnetwork_idipv6pinholes) | Settings › Advanced networking › Reservations & port forwarding |
| [`PUT {pinhole_url}`](#put-pinhole_url) | Settings › Advanced networking › Reservations & port forwarding |
| [`DELETE {pinhole_url}`](#delete-pinhole_url) | Settings › Advanced networking › Reservations & port forwarding |
| [`PUT /2.2/networks/{network_id}/power_saving`](#put-22networksnetwork_idpower_saving) | Settings › Advanced networking › Power saving |
| [`GET /2.2/networks/{network_id}/power_saving/schedules`](#get-22networksnetwork_idpower_savingschedules) | Settings › Advanced networking › Power saving |
| [`POST /2.2/networks/{network_id}/power_saving/schedules`](#post-22networksnetwork_idpower_savingschedules) | Settings › Advanced networking › Power saving |
| [`PUT {power_schedule_url}`](#put-power_schedule_url) | Settings › Advanced networking › Power saving |
| [`DELETE {power_schedule_url}`](#delete-power_schedule_url) | Settings › Advanced networking › Power saving |
| [`PUT /2.2/networks/{network_id}/flags/{flag_name}`](#put-22networksnetwork_idflagsflag_name) | Settings › Advanced networking › Network flags |
| [`GET /2.2/networks/{network_id}/backupinternet`](#get-22networksnetwork_idbackupinternet) | Settings › Advanced networking › Internet backup |
| [`GET /2.2/networks/{network_id}/backup_access_points`](#get-22networksnetwork_idbackup_access_points) | Settings › Advanced networking › Internet backup |
| [`PUT /2.2/networks/{network_id}/backupinternet`](#put-22networksnetwork_idbackupinternet) | Settings › Advanced networking › Internet backup |
| [`POST /2.2/networks/{network_id}/backup_access_points/connectivity_check`](#post-22networksnetwork_idbackup_access_pointsconnectivity_check) | Settings › Advanced networking › Internet backup |
| [`POST /2.2/networks/{network_id}/backup_access_points (add)`](#post-22networksnetwork_idbackup_access_points-add) | Settings › Advanced networking › Internet backup |
| [`PUT /2.2/networks/{network_id}/backup_access_points/{uuid} (edit)`](#put-22networksnetwork_idbackup_access_pointsuuid-edit) | Settings › Advanced networking › Internet backup |
| [`DELETE /2.2/networks/{network_id}/backup_access_points/{uuid}`](#delete-22networksnetwork_idbackup_access_pointsuuid) | Settings › Advanced networking › Internet backup |
| [`POST /2.2/networks/{network_id}/backup_access_points/rearrange`](#post-22networksnetwork_idbackup_access_pointsrearrange) | Settings › Advanced networking › Internet backup |
| [`PUT /2.3/networks/{network_id}/devices/secondary_wan_config`](#put-23networksnetwork_iddevicessecondary_wan_config) | Settings › Advanced networking › Internet backup |
| [`GET /2.2/networks/{network_id}/cellular_backup_usage`](#get-22networksnetwork_idcellular_backup_usage) | Settings › Advanced networking › Internet backup |
| [`GET /2.2/networks/{network_id}/cellular_backup_events`](#get-22networksnetwork_idcellular_backup_events) | Settings › Advanced networking › Internet backup |
| [`POST {network_url}/backup_access_points/ssid_discovery`](#post-network_urlbackup_access_pointsssid_discovery) | Settings › Advanced networking › Internet backup (discovery and order) |
| [`GET {network_url}/backup_access_points/ssid_discovery`](#get-network_urlbackup_access_pointsssid_discovery) | Settings › Advanced networking › Internet backup (discovery and order) |
| [`POST {network_url}/backup_access_points/rearrange`](#post-network_urlbackup_access_pointsrearrange) | Settings › Advanced networking › Internet backup (discovery and order) |
| [`GET /2.2/networks/{network_id}/wan/links`](#get-22networksnetwork_idwanlinks) | Settings › Advanced networking › Multi-WAN |
| [`PUT /2.2/networks/{network_id}/wan/enable_multi_wan`](#put-22networksnetwork_idwanenable_multi_wan) | Settings › Advanced networking › Multi-WAN |
| [`GET /2.2/networks/{network_id}/wan/discover`](#get-22networksnetwork_idwandiscover) | Settings › Advanced networking › Multi-WAN |
| [`GET /2.2/networks/{network_id}/wan/limits`](#get-22networksnetwork_idwanlimits) | Settings › Advanced networking › Multi-WAN |
| [`GET /2.2/networks/{network_id}/wan/links/{link_id}`](#get-22networksnetwork_idwanlinkslink_id) | Settings › Advanced networking › Multi-WAN |
| [`DELETE /2.2/networks/{network_id}/wan/links/{link_id}`](#delete-22networksnetwork_idwanlinkslink_id) | Settings › Advanced networking › Multi-WAN |
| [`POST /2.2/networks/{network_id}/wan/links (add)`](#post-22networksnetwork_idwanlinks-add) | Settings › Advanced networking › Multi-WAN |
| [`PUT /2.2/networks/{network_id}/wan/links (rename / reprioritize)`](#put-22networksnetwork_idwanlinks-rename--reprioritize) | Settings › Advanced networking › Multi-WAN |
| [`PUT /2.2/networks/{network_id}/settings (ISP settings (PPPoE / VLAN))`](#put-22networksnetwork_idsettings-isp-settings-pppoe--vlan) | Settings › Advanced networking › Internet connection › ISP settings (PPPoE / VLAN) |
| [`POST /2.2/networks/{network_id}/dns_perf_test`](#post-22networksnetwork_iddns_perf_test) | Settings › Advanced networking › DNS › DNS performance |
| [`GET /2.2/networks/{network_id}/dns_perf_test/{action_uuid}`](#get-22networksnetwork_iddns_perf_testaction_uuid) | Settings › Advanced networking › DNS › DNS performance |
| [`GET /2.3/networks/{network_id}/multistaticip`](#get-23networksnetwork_idmultistaticip) | Settings › Advanced networking › Multiple static IPs |
| [`PUT /2.3/networks/{network_id}/multistaticip`](#put-23networksnetwork_idmultistaticip) | Settings › Advanced networking › Multiple static IPs |
| [`GET /2.2/networks/{network_id}/subnets_config`](#get-22networksnetwork_idsubnets_config) | Settings › Additional wifi networks |
| [`PUT /2.2/networks/{network_id}/subnets_config`](#put-22networksnetwork_idsubnets_config) | Settings › Additional wifi networks |
| [`DELETE /2.2/networks/{network_id}/subnets_config/{subnet_type}`](#delete-22networksnetwork_idsubnets_configsubnet_type) | Settings › Additional wifi networks |
| [`GET /2.2/networks/{network_id}/subnets_config/{subnet_id}/dns_policies/content_filters`](#get-22networksnetwork_idsubnets_configsubnet_iddns_policiescontent_filters) | Settings › Additional wifi networks |
| [`PUT /2.2/networks/{network_id}/subnets_config/dns_policies/content_filters`](#put-22networksnetwork_idsubnets_configdns_policiescontent_filters) | Settings › Additional wifi networks |
| [`GET /2.2/networks/{network_id}/captive_portal/configurations`](#get-22networksnetwork_idcaptive_portalconfigurations) | Settings › Business guest wifi (captive portal) |
| [`PUT /2.2/networks/{network_id}/captive_portal/configuration (visitor fields)`](#put-22networksnetwork_idcaptive_portalconfiguration-visitor-fields) | Settings › Business guest wifi (captive portal) |
| [`PUT /2.2/networks/{network_id}/captive_portal/configuration (branding)`](#put-22networksnetwork_idcaptive_portalconfiguration-branding) | Settings › Business guest wifi (captive portal) |
| [`PUT /2.2/networks/{network_id}/captive_portal/subnet_id/GUEST`](#put-22networksnetwork_idcaptive_portalsubnet_idguest) | Settings › Business guest wifi (captive portal) |
| [`GET /2.2/networks/{network_id}/file/import/upload_link`](#get-22networksnetwork_idfileimportupload_link) | Settings › Business guest wifi (captive portal) |
| [`PUT {signed_upload_url} (storage upload, not the eero API)`](#put-signed_upload_url-storage-upload-not-the-eero-api) | Settings › Business guest wifi (captive portal) |
| [`POST /2.2/networks/{network_id}/file/import/job`](#post-22networksnetwork_idfileimportjob) | Settings › Business guest wifi (captive portal) |
| [`PUT /2.2/networks/{network_id}/business_guest_network`](#put-22networksnetwork_idbusiness_guest_network) | Settings › Business guest wifi › Other networks |
| [`GET /2.2/login/temporary (eero Insight)`](#get-22logintemporary-eero-insight) | Settings › Business guest wifi › eero Insight |
| [`POST /2.2/zendesk/create`](#post-22zendeskcreate) | Help › Report an issue |
| [`GET /2.2/account/eero_support`](#get-22accounteero_support) | Help › Contact support |
| [`POST /2.2/conversations/warmup`](#post-22conversationswarmup) | Home › Chat › Warm-up |
| [`Shared message body`](#shared-message-body) | Home › Chat (eero assistant) |
| [`GET /2.2/conversations?networkId={network_id}&excludeEmpty=true`](#get-22conversationsnetworkidnetwork_idexcludeemptytrue) | Home › Chat (eero assistant) |
| [`GET /2.2/conversations/{conversation_id}`](#get-22conversationsconversation_id) | Home › Chat (eero assistant) |
| [`GET /2.2/conversations/recommended?networkId={network_id}`](#get-22conversationsrecommendednetworkidnetwork_id) | Home › Chat (eero assistant) |
| [`POST /2.2/conversations/token`](#post-22conversationstoken) | Home › Chat (eero assistant) |
| [`PUT /2.2/conversations/messages (streaming)`](#put-22conversationsmessages-streaming) | Home › Chat (eero assistant) |
| [`PUT /2.2/conversations/feedbacks`](#put-22conversationsfeedbacks) | Home › Chat (eero assistant) |
| [`GET /2.2/premium/customer (Billing and payment hand-off)`](#get-22premiumcustomer-billing-and-payment-hand-off) | Billing and payment hand-off |
| [`GET /2.2/login/temporary?client_type=WEB_PAYMENTS&target_page={target_page}`](#get-22logintemporaryclient_typeweb_paymentstarget_pagetarget_page) | Billing and payment hand-off |
| [`GET /2.2/organizations/self/payment_account/management_url`](#get-22organizationsselfpayment_accountmanagement_url) | Billing and payment hand-off |
| [`POST /2.2/organizations/self/payment_account`](#post-22organizationsselfpayment_account) | Billing and payment hand-off |
| [`Billing completion callbacks`](#billing-completion-callbacks) | Billing and payment hand-off |
| [`Browser: GET https://www.amazon.com/ap/signin?{query}`](#browser-get-httpswwwamazoncomapsigninquery) | Sign-in through outside services |
| [`POST https://api.amazon.com/auth/token`](#post-httpsapiamazoncomauthtoken) | Sign-in through outside services |
| [`Browser: https://account.eero.com/`](#browser-httpsaccounteerocom) | Sign-in through outside services |
| [`GET https://sso.eero.com/api/v1/providers?assetType=mdpi`](#get-httpsssoeerocomapiv1providersassettypemdpi) | Sign-in through outside services |
| [`Browser: GET https://sso.eero.com/?idp={idp}&redirect={redirect}&clientID={client id}`](#browser-get-httpsssoeerocomidpidpredirectredirectclientidclient-id) | Sign-in through outside services |
| [`GET /2.2/partners/guardian/networks/{network_id}/availability`](#get-22partnersguardiannetworksnetwork_idavailability) | Guardian VPN |
| [`POST /2.2/partners/guardian/networks/{network_id}/pet`](#post-22partnersguardiannetworksnetwork_idpet) | Guardian VPN |
| [`POST /2.2/partners/guardian`](#post-22partnersguardian) | Guardian VPN |
| [`GET https://connect-api.guardianapp.com/api/v1.3/servers/all-server-regions/city`](#get-httpsconnect-apiguardianappcomapiv13serversall-server-regionscity) | Guardian VPN |
| [`POST https://connect-api.guardianapp.com/api/v1.3/servers/hostnames-for-region`](#post-httpsconnect-apiguardianappcomapiv13servershostnames-for-region) | Guardian VPN |
| [`POST https://connect-api.guardianapp.com/api/v1.2/subscriber-credential/create`](#post-httpsconnect-apiguardianappcomapiv12subscriber-credentialcreate) | Guardian VPN |
| [`POST https://{gateway}/api/v1.4/device-credentials`](#post-httpsgatewayapiv14device-credentials) | Guardian VPN |
| [`POST https://wifi-connect-api.guardianapp.com/api/v1.2/partners/subscriber/devices/list`](#post-httpswifi-connect-apiguardianappcomapiv12partnerssubscriberdeviceslist) | Guardian VPN |
| [`POST https://wifi-connect-api.guardianapp.com/api/v1.2/partners/subscriber/device/delete`](#post-httpswifi-connect-apiguardianappcomapiv12partnerssubscriberdevicedelete) | Guardian VPN |
| [`GET https://download.wireguard.com/windows-client/wireguard-installer.exe`](#get-httpsdownloadwireguardcomwindows-clientwireguard-installerexe) | Guardian VPN |
