# Security Policy

## Supported versions

| Version | Supported |
| --- | --- |
| 0.1.x | Yes |
| 0.0.x | No. Upgrade to 0.1.0; see CHANGELOG.md for the migration. |

0.0.x releases depend on MongoDB.Driver 3.4.0, which carries two known transitive advisories
(GHSA-pggp-6c3x-2xmx in Snappier, high; GHSA-6c8g-7p36-r338 in SharpCompress, moderate). They are fixed in
0.1.0 and will not be patched on the 0.0.x line.

## Reporting a vulnerability

Do not open a public issue.

Report privately through GitHub's
[security advisory form](https://github.com/thalysmarciobn/SharpMongoRepository/security/advisories/new).
If that is unavailable, contact the repository owner directly through their GitHub profile.

Please include:

- A description of the issue and what an attacker could achieve.
- The affected version.
- Steps to reproduce, or a proof of concept.
- Any mitigation you are aware of.

You can expect an acknowledgement within a few days and an assessment within two weeks. Where a fix is
warranted, it is released as a patch version with a published advisory crediting you, unless you ask
otherwise.

## Scope

In scope: anything in this library that lets a caller bypass an intended guarantee, leak data across
tenants or sessions, or inject into a query or index definition.

Out of scope, and better reported upstream:

- Vulnerabilities in MongoDB.Driver or the MongoDB server itself. Report those to
  [MongoDB](https://www.mongodb.com/docs/manual/administration/security/) and, if the library needs a
  version bump to pick up the fix, open a normal issue here.
- Insecure configuration in the example application under `Examples/`. It is illustrative, not hardened:
  its compose file starts an unauthenticated MongoDB on localhost, which is appropriate for a local demo
  and for nothing else.

## Dependencies

Package versions are centralised in `Directory.Packages.props`, so a transitive advisory is fixed in one
place. CI fails the build on any vulnerable package, transitive ones included, and Dependabot opens weekly
update pull requests.

## Notes for users

A few library behaviours are worth knowing when threat-modelling an application built on it:

- **Connection strings carry credentials.** Keep them in a secret store, not in `appsettings.json`. The
  registration extension takes the connection string as a plain argument, and it is also used as the DI
  service key for the client, so avoid logging the keys of resolved services.
- **Driver logging can include command contents.** Wiring an `ILoggerFactory` into the container enables
  driver diagnostics; set the `MongoDB.Command` category above `Debug` in production if your documents hold
  sensitive fields.
- **Server errors are not wrapped.** Driver exception messages can contain fragments of the failing
  document, for example the duplicate value in a duplicate-key error. Do not return them to end users.
