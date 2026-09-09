# Publishing Loadstone under Odinova vs Individual — analysis and recommendation

**Date:** 9 September 2026  
**Audience:** Morten Brudvik / Odinova AS  
**Context:** Loadstone for Windows is already submitted under an **Individual** Partner Center account (publisher display name **Morten Brudvik**, Store ID `9NJZ0KVVD5N2`, Submission `1152921505701847488`, currently **In certification**).

This note compares publishing as **Individual** vs **Company (Odinova)**, what a company move entails, fees, and a concrete recommendation.

---

## Executive recommendation

1. **Do not cancel or restart the current Individual submission.** Let certification finish and (if it passes) ship v1 under Morten Brudvik.
2. **Open an Odinova Company developer account in parallel** via the free company onboarding flow at [storedeveloper.microsoft.com](https://storedeveloper.microsoft.com) — registration fee is **$0** for company accounts as of May 2026.
3. **Plan the company brand for the next release or a deliberate migration**, not for this first certification queue. Prefer Odinova long-term if Loadstone is (or will be) part of Odinova’s business.

**Why:** Company is the better *publisher identity* for a real company brand, and Microsoft’s own App Developer Agreement expects a Company account when distribution is “in relation to your business, trade, or profession.” But Individual → Company is **not** an in-place upgrade; it is a **new account**, new package identity, rebuild, and either a support-mediated transfer or a new Store listing. Throwing away an in-flight `runFullTrust` human review for branding is expensive and unnecessary.

---

## Account types (what Microsoft says)

| | Individual | Company |
| --- | --- | --- |
| **Who it’s for** | Hobby / personal / distribution **not** in relation to business, trade, or profession | Businesses, orgs, freelancers whose Store work **is** in relation to business/trade/profession |
| **Store shows publisher as** | Your personal publisher display name (e.g. Morten Brudvik) | Registered legal business name (e.g. Odinova AS) |
| **Sign-in for new onboarding** | Personal Microsoft account (MSA) only | MSA **or** Microsoft Entra ID (work account) |
| **Verification** | Government ID + selfie | Business verification (D-U-N-S recommended) + employment/domain verification + mandatory due diligence |
| **Can convert Individual → Company?** | **No.** Not supported. Must create a **new** Company account. |

Sources: [Open a developer account](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/open-a-developer-account), [ADA v8.11](https://cdn-dynmedia-1.microsoft.com/is/content/microsoftcorp/microsoft/store/documents/legal/ada/fy26/MS.Store.ADAv8.11.US.EN.pdf) (account type declaration).

---

## Fees and money

### Registration / onboarding

| Fee | Individual (new flow) | Company (new flow) |
| --- | --- | --- |
| One-time developer registration | **$0** (was $19; waived) | **$0** (was **$99**; removed May 2026) |
| How to get the free flow | Start at [storedeveloper.microsoft.com](https://storedeveloper.microsoft.com) | Same entry point — **required** for the free/new flow |
| Legacy Partner Center signup | May still show older paid flows | Same — avoid legacy paths |

Microsoft blog (7 May 2026): company onboarding fee removed; Entra ID signup added.  
Individual free registration announced earlier (Sep 2025).

**Important:** Existing accounts do not need to “re-buy” anything. Free registration applies to **new** enrollments through the Store marketing page.

### Ongoing Store commerce fees (same for both account types)

Loadstone is **free** today → **no Store commission** on the download itself.

If you later sell the app or IAP via **Microsoft commerce**:

- Non-game apps: typically **15%** Store fee on Net Receipts (per current ADA / Store docs).
- Non-game apps using **your own** commerce (allowed for non-games): you can keep **100%** of that revenue (subject to your own payment provider / tax).

Account type does **not** change the revenue-share table. Company vs Individual is about **identity, verification, and compliance**, not a cheaper cut.

### Other costs you might still pay (outside Microsoft)

- **D-U-N-S number:** free option from Dun & Bradstreet (recommended for faster company verification).
- **Norwegian company docs / Brønnøysund** if you verify by documents instead of D-U-N-S.
- **Work email on odinova.ai** (or whatever company domain) — required for smooth employment verification.
- **Tax / payout profile** setup when you take money (same class of work for both account types).
- **Engineering time** to rebuild MSIX with the new `Publisher` CN / `PublisherDisplayName` / identity Name.
- **Support ticket effort** if you ask Microsoft to transfer a published product between accounts (not self-service).

---

## What publishing under Odinova entails (step by step)

You cannot flip the existing Individual account to Company. Path:

### A. Create the Company account

1. Go to **https://storedeveloper.microsoft.com** (not a random Partner Center deep link).
2. Choose **Company account (free)**.
3. Sign in with MSA or preferably an **Odinova Entra / work** account if you have one.
4. Business details:
   - Prefer entering a **D-U-N-S** for Odinova AS (fast path), **or**
   - Upload formation / registry / tax documents (manual review, often **2–5 business days**; DSA “Trader” rules also expect DUNS or accepted docs).
5. Contacts:
   - Work email on the **company domain** (e.g. `…@odinova.ai`). Personal Gmail/Yahoo not accepted for verification email.
   - Support contact details that can appear on Store listings (address/phone/email — DSA trader disclosure expectations apply).
6. Accept agreements; wait for **mandatory due diligence**, **business verification**, and **employment verification**. Manual steps: email OTPs, possible document appeals (**up to three** appeals per verification type).
7. After verification completes, finish setup → Apps & Games workspace.

**Typical calendar time:** hours if D-U-N-S + matching domain email sail through; **several business days to a couple of weeks** if documents, domain mismatch, or appeals.

### B. New product identity (this is the expensive part technically)

A Company account gets its **own** publisher identity. For Loadstone you would:

1. Reserve the name **Loadstone** again under the **Odinova** account (if still available there — name reservations are per account / product; you may need support help if conflicted with the Individual reservation).
2. Copy the new identity values from Product identity:
   - `Package/Identity/Name`
   - `Package/Identity/Publisher` (`CN=…` — **different** from today’s `CN=E6ED6F6D-88CB-42AC-BED3-B0FA24EC28DD`)
   - `Package/Properties/PublisherDisplayName` (expect **Odinova AS** or the legal name Microsoft verified)
   - New **Store ID**
3. Put those into `packaging/msix/AppxManifest.xml` and rebuild:
   ```powershell
   & 'C:\Program Files\PowerShell\7\pwsh.exe' -File C:\code\projects\loadstone-win\build\build-msix.ps1
   ```
4. Re-run Partner Center submission (listing, screenshots, privacy URL, age rating, `runFullTrust` justification again on first company submission).

**You cannot keep the old `Publisher` CN on packages uploaded to the company account.** Identity must match the account that owns the product.

### C. What happens to the current Individual listing?

Options after (or instead of) shipping on Individual:

| Option | What it means | Pros | Cons |
| --- | --- | --- | --- |
| **Ship on Individual, later transfer** | Ask Partner Center support to move the product to the Company account | May preserve Store listing continuity better than a brand-new app (case-by-case; Microsoft-mediated) | Not self-service; timeline opaque; publisher identity still becomes the company’s; package must match new publisher |
| **Ship on Individual, publish a new Company product** | Second Store entry under Odinova | Full control; clean company brand | Two listings risk; users on the Individual app don’t auto-move; you may want to unpublish the old one later |
| **Cancel Individual now, only ship Company** | Abandon Submission `1152921505701847488` | One clean brand from day one | Loses current certification queue + `runFullTrust` human review already in progress; weeks of delay |

There is **no documented self-service “change publisher”** for a live Store app. Transfers go through **Partner Center Help → Contact Support**. Gaming has a formal “reparenting” process; Apps & Games transfers are support-ticket driven and outcomes vary. Do not assume Package Family Name / update chain is preserved until Microsoft confirms in writing for your case.

---

## Pros and cons for Loadstone / Odinova

### Pros of Company (Odinova)

- **Correct account type** if Loadstone is part of Odinova’s business (ADA expects Company when distribution is in relation to trade/profession).
- **Brand:** Store page shows Odinova, not a personal name — better if you want a product line under odinova.ai.
- **Team access:** Entra tenant onboarding + Partner Center roles (Owner / Manager / Developer) for colleagues or future agents/automation under company IAM.
- **Trust / B2B:** Enterprises and directories sometimes prefer a company publisher.
- **Asset hygiene:** IP, payouts, and tax sit with the company rather than mixing personal and business.
- **Exit / hire:** Easier to hand off or sell a product line owned by the company account.
- **Fee:** Company registration is now **free** (was a $99 friction point).

### Cons of Company (Odinova)

- **Heavier verification** (D-U-N-S or docs, domain email, due diligence, possible phone/email verification of company approver).
- **Public trader contact details** expectations (address / phone / email on or behind Store compliance).
- **Cannot convert** the existing Individual account — duplicate account + migration work.
- **Package rebuild** mandatory (new CN / identity).
- **First `runFullTrust` review again** on the company product (human review delay).
- **Risk to the current submission** if you abandon it for branding.
- **Transfer uncertainty** if you later want one continuous Store listing.

### Pros of staying Individual (current)

- **Already done:** account verified, name reserved, package identity baked, submission in certification.
- **Faster to first users** for a free utility.
- **Simpler** personal ID verification (already completed).

### Cons of staying Individual long-term

- Publisher reads as a person, not Odinova.
- ADA wording pushes Company when the app is business-related — Individual is a mismatch if Odinova owns/markets Loadstone commercially.
- Harder later to put company banking, teammates, and brand on the same publisher without a migration.
- Personal name on a product you may want under the company brand forever.

---

## Loadstone-specific facts (current Individual submission)

| Item | Value |
| --- | --- |
| Account type | Individual |
| Publisher display name | Morten Brudvik |
| Package Identity Name | `MortenBrudvik.Loadstone` |
| Publisher CN | `CN=E6ED6F6D-88CB-42AC-BED3-B0FA24EC28DD` |
| Store ID | `9NJZ0KVVD5N2` |
| Submission | `1152921505701847488` — In certification, auto-publish |
| Pricing | Free, all markets |
| Privacy | `https://mortenbrudvik.github.io/loadstone-site/privacy/` |

Any company move invalidates uploading this same MSIX to an Odinova product without a rebuild against Odinova’s identity values.

---

## Recommended plan (concrete)

### Now (this week)

1. **Leave** Submission `1152921505701847488` alone — no cancel.
2. Decide whether Loadstone is an **Odinova product** commercially (brand, IP, future paid features, support under odinova.ai). If yes → Company is the right *end state*.
3. Gather for company onboarding:
   - Legal name / address of **Odinova AS** exactly as registered
   - D-U-N-S (lookup or free request) **or** recent Brønnøysund / formation docs
   - Work mailbox on **odinova.ai** (individual, not a shared alias — OTP required)
   - Support phone / address you’re willing to use for Store/trader disclosure

### Next (parallel, no rush vs cert)

4. Create **Company** account at [storedeveloper.microsoft.com](https://storedeveloper.microsoft.com).
5. Do **not** reserve Loadstone under Odinova until you’ve chosen migrate-vs-new-listing (avoid name fights). If you only want readiness, finish verification first.

### After Individual v1 is Published (or if cert fails)

6. **Preferred if Microsoft will transfer:** open a support ticket from the **source** (Individual) account requesting product transfer / reparent to Odinova Company; get written confirmation about Store ID / update continuity; then rebuild MSIX to the **destination** publisher identity and publish.
7. **Fallback:** reserve Loadstone (or an agreed name) under Odinova, new Store ID, rebuild, submit; unpublish or leave a “moved” note on the Individual listing if needed.
8. Update privacy / support contacts to company addresses if desired.

### What not to do

- Don’t abandon the current cert solely to change the publisher string.
- Don’t invent identity values — only Partner Center’s Product identity page.
- Don’t sign the MSIX for Store upload (Store re-signs).
- Don’t use legacy paid signup paths; always start at storedeveloper.microsoft.com for free company onboarding.

---

## Bottom line

| Question | Answer |
| --- | --- |
| Is company better for Odinova long-term? | **Yes**, if Loadstone is a company product. |
| Should the **current** submission be under Odinova? | **No** — too late / too costly mid-cert. |
| Registration fee for company? | **$0** via the new flow (was $99). |
| Extra Store cut for company? | **No** — same commerce rules; free app pays $0 either way. |
| Can we rename Individual → Company in place? | **No** — new Company account required. |
| Recommendation | **Ship v1 as Morten Brudvik; stand up Odinova Company account next; migrate or republish under Odinova for the following release.** |

---

## References

- [Open a Microsoft Store developer account](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/open-a-developer-account)
- [Company account verification / DSA trader docs](https://learn.microsoft.com/en-us/windows/apps/publish/store-business-verification-reqs)
- [Company free registration announcement (7 May 2026)](https://blogs.windows.com/windowsdeveloper/2026/05/07/publish-to-microsoft-store-as-a-company-now-with-free-registration-and-faster-onboarding/)
- [Individual free registration (10 Sep 2025)](https://blogs.windows.com/windowsdeveloper/2025/09/10/free-developer-registration-for-individual-developers-on-microsoft-store/)
- [Why distribute via Store (revenue options)](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/why-distribute-through-store)
- [ADA v8.11 (account type + Store fees)](https://cdn-dynmedia-1.microsoft.com/is/content/microsoftcorp/microsoft/store/documents/legal/ada/fy26/MS.Store.ADAv8.11.US.EN.pdf)
- Entry point: https://storedeveloper.microsoft.com

*Repo companion: `docs/store/grok-brief.md`, `docs/store-submission-runbook.md`.*
