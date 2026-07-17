# Store compliance notes

Researched against the live Microsoft Store Policies (v7.19, effective
2025-10-14) and current Google Play accessibility policy -- not from memory,
fetched directly. This is a working checklist, not a certification result:
no formal certification test can run against Phase 1, since there is no
packaged, installable, running app yet, and no Windows machine in this dev
loop to run one on.

## Windows / Microsoft Store

Source: https://learn.microsoft.com/en-us/windows/apps/publish/store-policies

- **Policy 10.2.8** -- *"Unsupported methods include but are not limited to
  use of accessibility APIs or undocumented or unsupported APIs in
  unsupported ways"* to modify a user's Windows experience. `SendInput` and
  `XInputGetState` are fully documented, supported Win32 APIs (used by
  legitimate macro/streaming/remote-control/accessibility tools already on
  the Store), so this app is not automatically disqualified -- but it puts
  this app squarely in a review-scrutiny category. Mitigation is accurate,
  unambiguous representation: the listing must describe exactly what it
  does (reads a game controller, simulates keyboard/mouse input for
  accessibility) per **10.1.1**, not soften or obscure it.
- **Policy 10.5.1** -- privacy policy is **mandatory**, not optional, for any
  product accessing Personal Information; explicitly names Win32 products.
  This app captures microphone audio, so this applies unconditionally.
- **Policy 10.5.2** -- transmitting a user's Personal Information (their
  voice, via the mic) to an outside service (Groq) requires **opt-in
  consent**, with an explicit in-product description of what's sent, to
  whom, and a way to revoke it. **Not built yet** -- Phase 2 needs a consent
  dialog before the first Groq call, not just a privacy-policy document.
- **Policy 10.3.1/10.3.2** -- the product must be testable by Microsoft's
  reviewers. This app requires a user-supplied Groq API key
  (`GROQ_API_KEY`/`groq_api_key.txt`) with no key baked in (correctly --
  never ship a shared key). At submission, a working demo key must go in
  Partner Center's "Notes for certification" field, or reviewers can't
  exercise the dictation path at all.
- **Policy 10.4.2** -- must handle exceptions and remain responsive. Found
  and fixed one real gap: `VoiceDictationService.Toggle()` was `async void`
  with no exception handling, so a missing API key would have crashed the
  whole app on the very first press. Fixed 2026-07-17 (see git log) --
  independent of certification, this was just a real bug worth catching now.

## Android / Google Play

Source: https://support.google.com/googleplay/android-developer/answer/10964491

- Google Play only permits `AccessibilityService` for apps that "help people
  with disabilities access their device or overcome challenges stemming
  from their disabilities." Using it for general automation is explicitly
  **not allowed**.
- Stricter enforcement took effect **2026-01-28** -- already in force as of
  this writing, not a future deadline.
- This product's actual use case (voice + controller control for users who
  can't use a mouse/keyboard) is a legitimate fit for the accessibility-tool
  category -- but that has to be the Play Console *declaration*, matched by
  an honest in-app description, not assumed. If not declared as an
  accessibility tool, Play now requires disclosing whether accessibility
  data is collected/shared and a short video demonstrating an in-app
  disclosure statement.

## Status

Done:
- [x] Async exception-safety fix (VoiceDictationService.Toggle)

Still required before either store submission (not attempted here -- these
are product/legal/UX decisions, not code):
- [ ] Privacy policy document (Windows: mandatory; Play: effectively mandatory too)
- [ ] In-app opt-in consent flow before first Groq call (Windows policy 10.5.2)
- [ ] Accurate, unsoftened store-listing description of controller-reading + input-simulation behavior
- [ ] Google Play accessibility-tool declaration matched to actual usage
- [ ] A demo Groq API key set aside for Microsoft certification notes
