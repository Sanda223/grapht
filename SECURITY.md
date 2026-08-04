# Security Policy

## Reporting a vulnerability

Please **do not** open a public issue for security problems. Email
**sandaru3940@gmail.com** with the details and steps to reproduce; you should get a
response within a few days.

## Scope notes

- `grapht view` serves source-derived data over HTTP. It binds **127.0.0.1 only**, by
  design, and must never be reachable off-machine. A change that causes it to bind any
  other interface is a security bug — report it.
- The viewer renders labels and ids extracted from parsed source code. All
  interpolations into the DOM are escaped; an unescaped interpolation is an XSS vector
  and a security bug — report it.
- `grapht` parses untrusted repositories by design (that is its job). Crashes on
  malformed input are ordinary bugs; code execution triggered by parsed content is a
  security bug.
