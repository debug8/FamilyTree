# How to add a new language

The app supports two ways to add a language. **Method B (JSON)** requires no rebuild and works even for languages/dialects without a system culture. **Method A (.resx)** compiles the language into the app as a "built-in".

All text keys come from `FamilyTree.App/Resources/Strings.resx` (Ukrainian is the base language). Keys missing from a translation automatically fall back to Ukrainian, so the app won't break on an incomplete translation.

---

## Method B — JSON file (fast, no build)

This method is backed by the `CustomLanguageFile` class; files are picked up at startup from:

```
%AppData%\FamilyTree\languages\<code>.json
```

where `<code>` is the language identifier (the file name without extension), e.g. `pl`, `de`, `uk-poltava`, `elvish`.

Steps:

1. Create the folder `%AppData%\FamilyTree\languages\` if it doesn't exist.
2. Add a `<code>.json` file like this:

```json
{
  "displayName": "Polski",
  "formattingCulture": "pl",
  "strings": {
    "MainWindow_Title": "Drzewo genealogiczne",
    "Menu_File": "Plik",
    "Menu_New": "Nowy",
    "Menu_Open": "Otwórz…",
    "Tab_Tree": "Drzewo",
    "Tab_WhoIsWho": "Kto kim jest"
  }
}
```

- `displayName` — the name shown in the language switcher (if omitted, the code is shown).
- `formattingCulture` — the culture used for dates/numbers (e.g. `pl`, `en`). If empty or unknown, a fallback culture is used.
- `strings` — "resource key → translation" pairs. See `Strings.resx` for the list of keys. You only need to translate the ones you want; the rest fall back to Ukrainian.

3. Launch the app — the language appears in the switcher.

> Tip: to get the full list of keys, take every `name="…"` from `Strings.resx` and fill in the values in your language. A malformed JSON file is silently ignored (it won't break startup), so validate your file.

---

## Method A — built-in language (.resx, requires a build)

Best for languages with a real `CultureInfo` (e.g. `pl`, `de`, `fr`).

1. Create a satellite resource `FamilyTree.App/Resources/Strings.<code>.resx` (e.g. `Strings.pl.resx`) by copying all keys from `Strings.resx` and translating the values.
2. Register the language in `FamilyTree.App/Localization/LocalizationService.cs` — in the constructor, next to the existing ones:

```csharp
RegisterBuiltIn("uk");
RegisterBuiltIn("en");
RegisterBuiltIn("pl");   // ← new language
```

3. Rebuild the project (`dotnet build`). The language appears in the switcher; its name is taken from `CultureInfo.NativeName`.

---

## Important: kinship (relationship) names

The steps above translate the **UI**. However, **kinship names** (badges on the tree, the "Who is who" tab) are generated in code by dedicated formatters — not from resx/JSON.

The dispatcher `FamilyTree.App/Kinship/CultureKinshipFormatter.cs` currently picks: `en` → English formatter, any other language → **Ukrainian**. So a new language gets a translated UI, but relationship names stay in Ukrainian (as a fallback).

To fully localize kinship names too:

1. Add an `IKinshipFormatter` implementation for your language in `FamilyTree.Domain/Kinship/` (following `UkrainianKinshipFormatter` / `EnglishKinshipFormatter`; the algorithm is shared — the formatter only turns a `KinshipContext` into a string).
2. Wire it into `CultureKinshipFormatter` (add a branch by language code and forward `Style`).
3. Add tests in `FamilyTree.Tests/Domain/` following the existing formatter tests.

---

## In short

- Just translate the UI, quickly and without a build → **JSON** in `%AppData%\FamilyTree\languages\`.
- Ship the language inside the app → **.resx** + `RegisterBuiltIn`.
- Also want correct relationship names → an extra **`IKinshipFormatter`** + a branch in `CultureKinshipFormatter`.
