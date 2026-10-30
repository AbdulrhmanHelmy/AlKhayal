# Owner public keys

Production public key `rsa-17b43bcb5933edf6b1e9463d.xml` was integrated from the owner's existing public export after the owner initialized their signing key. Only public RSA Modulus and Exponent are embedded; no private key/password is present or needed to verify licenses.

New trusted public exports must be named `<SigningKeyId>.xml`. The build rejects private material. Preserve old trusted public keys during rotation so existing perpetual licenses continue working. Rebuild and restart POS after changing embedded public keys; an already running process retains its old assembly/key registry.

See docs/phase-10/phase-10-1-licensing.md and docs/phase-10/production-public-key-integration.md.
