# FamilyTree — аудит коду (2026-07-26)

## Статус виправлень

- ✅ **Фікс 1 — стійкість до битих файлів.** Закриті: S-C2, S-C3, S-B4 (частково: читання), S-D16, D-D15, D-B9 (нормалізація при завантаженні), A-D24, A-B12 (`SafeFormat`), A-D25 (`RemoveRecent` лише при `NotFound`). Нове: `DocumentIntegrity`, `FamilyFileException`, `FileErrorKeys`, `DocumentIssue`, `FamilyDocument.RepairedIssues`, тести `CorruptFileTests` (19 кейсів). Побічно виправлено `tools/SeedGenerator` — він писав пари подружжя в ненормалізованому порядку (114 з 235 у `rodyna-500`).
- ⬜ Решта — нижче.


Аудит трьох шарів: `Domain`, `Storage`, `App` (WPF). Нижче — підтверджені знахідки з посиланнями на файл:рядок.
Позначки: **C** = критично, **B** = важливо, **D** = дрібне, **P** = покращення.

---

## TOP-10 — з цього варто почати

| # | Пункт | Шар | Чому першим |
|---|-------|-----|-------------|
| 1 | Дублікати/порожні `Person.Id` у файлі → `ArgumentException` при відкритті | Storage+App | Застосунок ламається на «поганому» файлі, документ уже встановлено → напівзламаний стан |
| 2 | `Persons.Clear()` скидає виділення → подвійний повний `Rebuild()` | App | Кожна дія коштує 2× перерахунок дерева + N розрахунків родства |
| 3 | O(N) `Compute()` у UI-потоці при перебудові дерева | App+Domain | Секунди фризу на 500 особах; ТЗ орієнтується на 5000 |
| 4 | Мачуха/вітчим/пасинок = «зв'язок не встановлено» | Domain | Видимий баг у власній демо-родині |
| 5 | Подружжя «зникає», якщо між подружжям є кровний зв'язок | Domain | Дружина показується як «двоюрідна сестра» |
| 6 | Merge не оновлює наявних осіб → правки родича тихо відкидаються | Storage | Ключовий генеалогічний сценарій втрачає дані, звіт при цьому «зелений» |
| 7 | `NullReferenceException` на JSON з явними `null` | Storage | «Object reference not set» замість «файл пошкоджено» |
| 8 | Placeholder пошуку не рендериться (`Tag` без шаблону) | App | Перекладений рядок, якого користувач не бачить |
| 9 | Помилки валідації особи не показуються, Save мовчки заблокований | App | Користувач не розуміє, чому не може зберегти |
| 10 | Пошук фільтрує → виділення й усе дерево зникає | App | Почав друкувати в пошуку — дерево пропало |

---

## 1. FamilyTree.Domain

### Критично

**D-C1. `Kinship/KinshipCalculator.cs:146-160` — нерідні батьки/діти не розпізнаються.**
`MapPatternA` покриває лише `(0,1)`, `(1,1)`, `(2,1)`; `MapPatternB` — лише `(1,0)`, `(1,1)`. «X — мій батько, B — його друга дружина» → `MapPatternA(1,0)` → `NotAffinity`. Зворотний бік → `MapPatternB(0,1)` → `NotAffinity`. Обидва напрямки дають `KinshipKind.None`.
Не теоретично: `DemoFamilyGenerator.AddHalfSibling` (129-141) створює саме таку конфігурацію — у демо-родині є особи з «немає зв'язку».
*Фікс:* `MapPatternA(1,0) → StepParent`, `MapPatternB(0,1) → StepChild`; додати `AffinityKind.StepParent/StepChild` + назви «вітчим/мачуха», «пасинок/пасербиця», «step-father/step-son».

**D-C2. `Kinship/KinshipCalculator.cs:34-49` — подружжя губиться при кровному зв'язку.**
`isSpouse` перевіряється лише в гілці `if (!nca.Found)`. Шлюб двоюрідних → `nca.Found == true` → «двоюрідна сестра» замість «дружина». `KinshipKind.Spouse` та `IsFormerSpouse` втрачаються назавжди; `KinshipPathExplainer` не показує подружнє ребро.
*Фікс:* перевіряти шлюб *до* NCA, або додати `IsAlsoSpouse` у `KinshipResult` → «дружина (також двоюрідна сестра)».

### Важливо

**D-B3. `Kinship/KinshipCalculator.cs:81-143` — свояцтво не враховує розлучення.**
Рядок 140: `IsFormerSpouse: false` зашито; при переборі сполучних осіб (88, 111) не викликається `graph.IsSpouseActive`. Мати колишньої дружини = «теща». Для прямого подружжя код це розрізняє (39) — поведінка суперечлива всередині одного класу.

**D-B4. `Kinship/KinshipCalculator.cs:162-178` — «напіврідність» на неповних даних.**
`shared.Count == 1` → `HalfPaternal/HalfMaternal` без перевірки, чи відомі обидва батьки. Двоє дітей із записаним лише батьком → «єдинокровний брат»: застосунок стверджує факт (різні матері), якого в даних немає. У тестовій родині так побудовані Влас, Ніна, Петро, Роман.
*Фікс:* `Half*` лише коли в обох ≥2 відомих батьків; інакше — `SiblingKind.Unknown`.

**D-B5. `Layout/TreeLayoutEngine.cs:74` — при кількох шлюбах показується не той партнер.**
```csharp
var spouse = graph.GetSpouses(personId).FirstOrDefault(s => !visited.Contains(s.Id));
```
Обирається перший партнер за порядком у файлі, не той, від кого діти в цій вітці. Решта партнерів не потрапляє в `positions` → `Finalize` (265) не малює до них ребро.
*Фікс:* юніт = пара батьків конкретної групи дітей; згрупувати `GetChildren` за другим батьком, окремий юніт на кожен шлюб.

**D-B6. Квадратична складність на гарячому шляху.**
`TreeViewModel.cs:212` кличе `Compute(root, person, …)` для кожного вузла. Кожен `Compute`: 2 BFS (`CollectAncestorDistances` — для root результат ідентичний у всіх n викликах) + `DetermineLineage` ще по BFS на кожного з батьків root (197) + `TryAffinity` по BFS на кожного партнера ≈ 5 BFS × n.
*Фікс:* `KinshipContextCache` — кешувати `AncestorDistances(root)` і множини «предки через батька/матір» один раз на (граф, root); `DetermineLineage` рахувати з наявних відстаней.

**D-B7. `FamilyGraph.cs:177-180` — алокація `List<Person>` на кожен виклик, у т.ч. в BFS.**
`GetParents` викликається для кожного вузла кожного BFS. n=5000 → десятки мільйонів короткоживучих `List` + LINQ-ітераторів.
*Фікс:* `IReadOnlyList<Guid> GetParentIds(Guid)`, BFS перевести на Id.

**D-B8. `Validation/RelationshipValidator.cs:49-66` — інваріант «1 батько + 1 мати» обходиться статтю `Unknown`.**
Умова містить `parent.Gender != Gender.Unknown`, порівняння лише за статтю → можна додати 5 біологічних батьків з `Unknown`. Псує `ClassifySiblings` (`shared >= 2` = повнорідні).
*Фікс:* жорсткий інваріант «≤2 біологічних батьків усього».

**D-B9. `Validation/RelationshipValidator.cs:110-111` + `SpouseLink.cs:11-14` — дубль шлюбу не виявляється при ненормалізованих Id.**
`Person1Id`/`Person2Id` — `required init` public, `new SpouseLink { Person1Id = b, Person2Id = a }` компілюється; `DocumentMapper.cs:104` покладається на припущення «у файлі вже нормалізовані». Файл із іншого джерела → дубль шлюбу проходить, ламає `_spouseActive`.
*Фікс:* нормалізувати в самому типі (private init + `Create`) або в `ToDomain`; у валідаторі порівнювати невпорядковано.

**D-B10. `Validation/RelationshipValidator.cs:123-135` — `ValidatePerson` ніде не викликається.**
Grep по `FamilyTree.App`: лише `ValidateParentChild` (×2) і `ValidateSpouse` (×1). Попередження «дата смерті раніша за народження» реалізоване й перекладене, але користувач його ніколи не побачить. Див. також App-B10.

### Дрібне

**D-D11.** `UkrainianKinshipFormatter.cs:33-35,142-149`, `EnglishKinshipFormatter.cs:28-30,122-126` — `Gender.Unknown` у подружжі/свояцтві тихо стає чоловічою (`Pick` викликається напряму, минаючи `ByGender`). Плюс `PivotGender == Unknown` дає різні дефолти в сусідніх рядках: `SpouseParent` → «свекор» (ніби чоловік), `SpouseSibling` → «шурин» (ніби жінка).

**D-D12.** `UkrainianKinshipFormatter.cs:99` — `SiblingKind.HalfUnknown` → «зведений брат». Українською «зведений» = дитина мачухи (без спільної крові). Термінологічна помилка в UI. → «неповнорідний брат».

**D-D13.** `CommonAncestorFinder.cs:36-47` — неоднозначні НСП вирішуються порядком у файлі. `IsCloser` не розрізняє `(1,3)` і `(3,1)` → другий предок молча відкидається. `FindNearest` (59) бере `AncestorIds[0]` → для повнорідних сиблінгів `KinshipPathExplainer` веде ланцюжок то через батька, то через матір. Після мерджу/пересортування файлу назва зв'язку може змінитися.

**D-D14.** `FamilyGraph.cs:42-52` — повторний шлюб із тією ж особою: `_spouseActive[pair] = link.IsActive` — «останній перемагає». Порядок «новий шлюб, потім старий» → подружжя вважається розлученим. *Фікс:* `active = links.Any(l => l.IsActive)`.

**D-D15.** `FamilyGraph.cs:28`, `RelationshipValidator.cs:24` — `ToDictionary(p => p.Id)` кидає `ArgumentException` на дублікатах Id. Валідатор, який мав би це діагностувати, падає з тією ж помилкою. Див. Storage-C3.

**D-D16.** `FamilyGraph.cs:30-40` — самозв'язок і цикли приймаються з файлу. Зависань немає (усі обходи мають `visited`), але при циклі A→B→A `Compute(A,B)` і `Compute(B,A)` обидва дають `DirectDescendant`. Окремо: `ParentRole` у граф не переноситься зовсім → прийомні батьки трактуються як біологічні (ТЗ 4.4). Латентно, бо UI `ParentRole` не виставляє.

**D-D17.** `Layout/TreeLayoutEngine.cs:185` — `group.OrderBy(id => order.IndexOf(id))`, `order` — `List<Guid>` з усіх n → O(n²). *Фікс:* `Dictionary<Guid,int>` індексів (2 рядки).

**D-D18.** `Layout/TreeLayoutEngine.cs:138-177` (`BuildFull`) — покоління визначається BFS «хто перший», а не за формулою ТЗ 5.1.3 (`StepsDown − StepsUp`). Для осіб, досяжних кількома шляхами, рівень залежить від порядку BFS → дід може стати в один рядок із root, ребро «батько-дитина» малюється горизонтально. Плюс `Math.Abs(g) > depthLimit` обмежує лише вертикаль.

**D-D19.** `Layout/TreeLayoutEngine.cs:242-283` (`Finalize`) — порядок вузлів/ребер із `Dictionary`/`HashSet`, не задано контрактом. Унеможливлює snapshot-тести, робить z-order нестабільним. *Фікс:* `OrderBy(depth, col, id)`.

**D-D20.** `Seeding/DemoFamilyGenerator.cs:62,87` — дрейф років народження, дати в майбутньому. `baseYear` крок `GenerationGap = 25`, фактичний — `_rnd.Next(24,43)` (сер. 33), і `childBirth` стає `ParentsBirthYear` наступної пари → +8 років/покоління. `Generations = 5` → остання генерація ≈ `currentYear + 16`; `= 8` → до `+40`.

**D-D21.** `DemoFamilyGenerator.cs:130` — двоєженство: `AddHalfSibling` укладає другий шлюб, і лише з 25% (183) він розлучений → у ~75% два чинні шлюби; порядку в часі немає.

**D-D22.** `DemoFamilyGenerator.cs:84,93,113,132` — `MaxPersons` перевищується: перевірка стоїть перед додаванням *пари* осіб. Тест `Respects_max_persons_cap` проходить випадково для конкретного насіння.

**D-D23.** `DemoFamilyGenerator.cs:205` — `_rnd.Next(1,28)` → дні 28-31 недосяжні. *Фікс:* `DateTime.DaysInMonth`.

**D-D24.** `DemoFamilyGenerator.cs:166-173` — дата смерті не узгоджується з дітьми/шлюбом; валідатор таких правил не має.

**D-D25.** `Kinship/Lineage.cs:5-6` vs `KinshipCalculator.cs:185-221` — док обіцяє `Unknown` для прямих/сиблінгів, реалізація дає `Paternal` для батька і `Mixed` для повнорідного сиблінга (закріплено тестом).

**D-D26.** `EnglishKinshipFormatter.cs:137` — `$"{n}th"` → «21th», «22th». `UkrainianKinshipFormatter.cs:172` («8-юрідний») відходить від ТЗ 4.6 («далекий родич»).

### Покращення

**D-P27.** Відсутні інваріанти: дитина після смерті батька (для матері +9 міс.); `MarriageDate` до народження / після смерті; `DivorceDate < MarriageDate`; попередження про інцест. `ValidateSpouse` не отримує `persons` — не може перевірити ні існування осіб, ні дати. `ValidateParentChild` при відсутніх Id тихо пропускає всі перевірки (`TryGetValue` без `else`).

**D-P28.** `RelationshipValidator.cs:141-148` (`IsAncestor`) — повна перебудова карти батьків на кожен виклик, O(n·L) при масовій перевірці.

**D-P29.** `KinshipPathExplainer.cs:59-60` — `Step()` кличе повний `Compute` (2+ BFS) на кожен крок; достатньо типу ребра між сусідами.

**D-P30.** `Person.cs:50-52` — `FullName` алокує масив + LINQ на кожне звернення (біндінги WPF + `KinshipPathExplainer.Step` двічі на крок).

**D-P31.** Немає `ArgumentNullException.ThrowIfNull` у ctor `KinshipCalculator` (13-17) і `KinshipPathExplainer` (13-17), на відміну від решти домену. `NearestCommonAncestors.Found` (`CommonAncestor.cs:21`) кине NRE при `AncestorIds = null`.

**D-P32.** StackOverflow у layout малоймовірний (глибина рекурсії = кількість *поколінь*), але `maxDepth = 0` = «без обмежень» → патологічний файл із тисячами поколінь дає неперехоплюваний `StackOverflowException`. Страховка: жорсткий ліміт 200 + лог.

---

## 2. FamilyTree.Storage

### Критично

**S-C1. `JsonFamilyStorage.cs:94,102` — «атомарність» не захищає від зникнення живлення.**
```csharp
await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
File.Replace(tempPath, fullPath, destinationBackupFileName: null);
```
`WriteAllTextAsync` не робить `FlushFileBuffers` — дані в кеші ОС. `File.Replace` атомарний щодо *метаданих*. При BSOD між записом і rename NTFS може зафіксувати перейменування, а блоки — ні → цільовий файл нульової довжини. Тест `Interrupted_save_does_not_corrupt_existing_file` перевіряє лише виняток у процесі.
Плюс `destinationBackupFileName: null` відмовляється від безкоштовного атомарного бекапу від ReplaceFile.
*Фікс:* `FileStream` з `FileOptions.WriteThrough` (або `Flush(flushToDisk: true)`), передати `destinationBackupFileName`.

**S-C2. `Serialization/DocumentMapper.cs:32,38-40` — NRE на JSON з явними `null`.**
Ініціалізатори в `FamilyFileDto` (`= new()`) не рятують: `System.Text.Json` за замовчуванням записує `null`, перекриваючи ініціалізатор. `DefaultIgnoreCondition.WhenWritingNull` (`JsonFamilyStorage.cs:26`) впливає лише на серіалізацію. Файл `{"schemaVersion":1,"meta":null,"persons":null}` → NRE, і `MainViewModel.cs:400` показує «Object reference not set…».
*Фікс:* `dto.Meta ?? new()`, `dto.Persons ?? []` + `InvalidDataException` з локалізованим текстом.

**S-C3. `JsonFamilyStorage.cs:69-72` + `DocumentMapper.cs:25-42` — нуль валідації цілісності після завантаження.**
- **Дублікати/відсутні Id.** `PersonDto.Id` — `Guid`, пропущене `"id"` → `Guid.Empty`; двоє без `id` → два `Guid.Empty` → `FamilyGraph.cs:28` `ToDictionary` кидає `ArgumentException`. Летить із обробника `DocumentChanged`, тобто **після** `_session.SetDocument(...)` — документ встановлено, частина підписників не виконалась, UI напівзламаний, кожна наступна зміна знову кидає виняток. Файл при цьому «відкритий» і його можна перезаписати.
- **Dangling links.** `FamilyGraph` їх ігнорує (33, 44 — добре), але вони лишаються в `FamilyDocument` і **записуються назад**; лічильники UI розходяться з графом, попередження немає.
- **Самозв'язок** `ParentId == ChildId` приймається.
- **Enum поза діапазоном:** `JsonStringEnumConverter` (27) за замовчуванням `allowIntegerValues: true` → `"gender": 7` стає `(Gender)7` і тече у форматери.

*Фікс:* крок `ValidateDocument` у `LoadAsync`: пусті/дубльовані Id → `InvalidDataException` (або перегенерація Id із переприв'язкою через `remap`), відкидання самозв'язків і dangling із підрахунком, `Enum.IsDefined`. Найдешевший спосіб закрити весь клас «файл зроблено вручну → застосунок ламається».

### Важливо

**S-B4. `JsonFamilyStorage.cs:52-60` — необроблені винятки виходять «як є», користувач бачить англійський технічний текст.**
Битий JSON → `JsonException` («'x' is an invalid start of a value. LineNumber: 3…»), а не `InvalidDataException`, хоч поруч кидаються українські. Заблокований файл / немає прав / видалено між діалогом і читанням → `IOException`/`UnauthorizedAccessException` летять напряму, `MainViewModel.cs:394-402` показує системний текст. `(int?)root["schemaVersion"]` кидає `InvalidOperationException`, якщо версія — рядок `"1"`, `1.5` або `true`. `schemaVersion: 0` → `InvalidOperationException("Немає міграції з версії схеми 0.")`.
*Фікс:* try/catch → власний `FamilyFileException` з inner; `root["schemaVersion"] is JsonValue v && v.TryGetValue<int>(out var version)`; вимагати `version >= 1`.

**S-B5. `JsonFamilyStorage.cs:90,112` — фіксоване ім'я temp + `TryDelete` у catch = race condition.**
```csharp
var tempPath = fullPath + ".tmp";
catch { TryDelete(tempPath); throw; }
```
Сховище — singleton (`App.xaml.cs:62`) без локу. Два паралельні `SaveAsync` (Ctrl+S під час збереження при закритті, майбутній автосейв, дві копії застосунку): другий відкриває `*.tmp` з `FileMode.Create` і **обрізає** файл, який перший готовий промоутити → у цільовий потрапляє обрізаний JSON. У зворотному порядку `TryDelete` видаляє temp сусіда. Цільовий файл не блокується — дві копії застосунку тихо перетирають зміни (lost update).
*Фікс:* унікальне ім'я (`+ Guid.NewGuid():N`), `SemaphoreSlim`, опційно lock-файл.

**S-B6. `JsonFamilyStorage.cs:99-107` — TOCTOU у `File.Move` і немає фолбеку для `File.Replace`.**
`File.Move` без `overwrite: true`: якщо між `File.Exists` і `Move` файл з'явився (інший екземпляр, OneDrive/Dropbox) → `IOException`, і в `catch` свіжозаписаний temp **видаляється** — робота втрачена. `File.Replace` падає на FAT32/exFAT-флешках, частині SMB-шар і синхронізованих папках; фолбеку немає → збереження туди не працює *завжди*.
*Фікс:* `File.Move(temp, full, overwrite: true)`; `catch (IOException | PlatformNotSupportedException)` → фолбек на Move.

**S-B7. `JsonFamilyStorage.cs:142` + `app.manifest` — довгі імена бекапів ламають збереження в глибоких теках.**
```csharp
var backupName = $"{fileName}.{DateTime.UtcNow.Ticks:D19}.{Guid.NewGuid():N}.bak";
```
≈66 символів до довжини шляху. У маніфесті немає `<longPathAware>true</longPathAware>` → для документа в `C:\Users\…\OneDrive - Company\…` шлях бекапу вилітає за ліміт. `BackupExisting` викликається **всередині** try до `File.Replace` → падає **усе збереження**, хоч документ записуваний.
*Фікс:* короткі імена (`.1.bak`…`.5.bak` або `yyyyMMdd-HHmmss`); `longPathAware` у маніфест; провал бекапу зробити некритичним (лог + продовжити).

**S-B8. Бекапи ніяк не використовуються — write-only функціонал.**
`.backups` згадується лише в `JsonFamilyStorage.cs`. Немає ні відновлення, ні підбирання «осиротілого» `*.tmp` після краху (а він містить *новіші* дані й тихо перезаписується наступним збереженням). Після пошкодження користувач має 5 копій із незрозумілими іменами у прихованій теці й жодної кнопки.

**S-B9. Міграції формату — мертвий код.**
Реалізацій `IFormatMigration` — **нуль**; `App.xaml.cs:62` реєструє `JsonFamilyStorage` без міграцій; тестів на `ApplyMigrations` немає. Ланцюжок (119-132) ніколи не виконувався. Уже видно проблеми: `root["schemaVersion"] = version` виставляється **після** `Migrate` (міграція не може покладатися на поле); порядок міграцій з однаковим `FromVersion` невизначений (`FirstOrDefault`).
Окремо: невідомі поля JSON **втрачаються** (немає `[JsonExtensionData]`) — файл із новішої збірки після round-trip старою версією тихо худне.

**S-B10. `FamilyMerger.cs:60-85` — злиття не оновлює наявних осіб, Id-колізії не обробляються.**
```csharp
if (existingIds.Contains(person.Id)) { remap[person.Id] = person.Id; duplicates++; continue; }
```
- «Дав копію родичу, той доповнив, зливаємо назад»: усі Id збігаються → **усі виправлення полів (дата смерті, місце, нотатки, дівоче прізвище) тихо відкидаються**, а звіт бодро повідомляє «додано 0, дублікатів N». Нові особи й зв'язки додаються — результат виглядає правдоподібним, втрату помітити важко.
- Якщо Id збігся у *різних* людей (детерміновані Id — саме такі обидва зразки в репо: `00000000-0000-4000-8000-…` і `10000000-…`), особу **проковтне як дублікат, а її зв'язки переприв'яже до сторонньої людини**. `Clone` (166-181) зберігає `Id = p.Id` — перенумерації при колізії немає в принципі.
*Фікс:* при збігу Id порівнювати `IdentityKey`; розбіжність → нова особа з **новим** Guid і переприв'язкою через `remap`; для справжніх дублікатів — заповнювати порожні поля цілі або показувати конфлікти.

### Дрібне

**S-D11. `JsonFamilyStorage.cs:22-28` — немає `Encoder`, уся кирилиця в escape-послідовностях.**
Реальний `samples/testova-rodyna.familytree`:
```json
"title": "\u0422\u0435\u0441\u0442\u043E\u0432\u0430 \u0440\u043E\u0434\u0438\u043D\u0430"
```
Round-trip коректний, але файл роздувається ~3× на кирилиці, його неможливо ні прочитати, ні продіфати, ні згрепати — при тому що формат позиціонується як «людиночитний JSON».
*Фікс:* `Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping` (безпечно — вивід не в HTML).

**S-D12. `JsonFamilyStorage.cs:142,150-157` — ротація не гарантує «5 найновіших».**
Гранулярність `DateTime.UtcNow` у Windows ≈15,6 мс → кілька збережень підряд дають однакові тики, і сортування далі порівнює **випадковий GUID**. Плюс `BackupExisting` викликається *до* промоуту, тому кожна **невдала** спроба з'їдає слот копією того самого вмісту — після 5 невдач історія знищена, усі 5 бекапів ідентичні.

**S-D13. `JsonFamilyStorage.cs:84` — CPU-серіалізація на UI-потоці.**
`JsonSerializer.Serialize(dto, JsonOptions)` — синхронно до першого await, тобто на потоці викликача. Плюс документ тримається і як рядок, і як буфер. *Фікс:* `SerializeAsync` у потік (заодно закриває S-C1). (`LoadAsync`, навпаки, коректно йде на пул-потік завдяки `ConfigureAwait(false)`.)

**S-D14. `JsonFamilyStorage.cs:80`** — `document.Meta.UpdatedAt = DateTime.UtcNow` до запису: після невдачі документ у пам'яті має час, який не відповідає нічому на диску. (`IsDirty` тут коректний — знімається лише після успіху, 116.)

**S-D15.** `MetaDto`/`PersonDto.CreatedAt/UpdatedAt` — `DateTime` без гарантії `Kind`. Пишеться `Z`, але читання значення *без* `Z` дає `Unspecified` і далі трактується як UTC → зсув на офсет. *Фікс:* `DateTimeOffset` або `SpecifyKind` при мапінгу.

**S-D16.** `File.ReadAllTextAsync(path)` коректно розпізнає BOM, запис — UTF-8 без BOM (правильно). Але файл, перезбережений у Notepad як «ANSI» (Windows-1251), декодується з `U+FFFD` **без помилки** — кирилиця тихо стає крякозябрами і в такому вигляді зберігається назад. *Фікс:* `new UTF8Encoding(false, throwOnInvalidBytes: true)`.

**S-D17.** `FamilyMerger.cs:122-127,93-97,116-120` — дублікат подружньої пари відкидається цілком разом із `MarriageDate`/`DivorceDate`, яких у цілі могло не бути. Зв'язки, чиї особи не потрапили в `remap`, тихо пропускаються — у `MergeReport` про них нічого немає.

**S-D18.** `FamilyMerger.cs:154-164` — `IdentityKey` (ПІБ + BirthDate) пропускає очевидні дублікати: немає нормалізації апострофів (`Ім'я` U+2019 vs U+0027), дефісів, подвійних пробілів. `Gender` не входить у ключ → однакове ПІБ+дата з різною статтю зливаються без попередження. Дівоче прізвище не враховується.

**S-D19.** `FamilyMerger.cs:134-143` — `Apply` = три `AddRange`, не перевіряє актуальність плану. Той самий `MergePlan` двічі → дублікати. Зараз не стріляє (між `Plan` і `Apply` модальний діалог, `MainViewModel.cs:248-260`), але інваріант не захищений. `Plan:51` `existingByKey.TryAdd`: якщо в цілі вже двоє з однаковим ключем, імпорт завжди зливається в першого.

**S-D20.** `MainViewModel.cs:401` — `OpenPathAsync` у `catch` робить `RemoveRecent(path)`: файл викидається з недавніх навіть при тимчасовій помилці (заблокований, мережа відпала).

### Покращення

**S-P21. Culture-sensitivity — проблем не знайдено.** `System.Text.Json` пише `DateTime`/`DateOnly`/`Guid`/`int` інваріантно; `FamilyMerger` використовує `ToLowerInvariant` і `birth.ToString("O")`; `RotateBackups` — `StringComparer.Ordinal`. Файл із `uk-UA` читається під `en-US`. Єдиний залишковий ризик — S-D15.

**S-P22.** `CancellationToken` пророблений у сховищі, але **ніколи не передається**: `MainViewModel.cs:239,378,394` кличуть із `default`. Довге збереження на мережевий диск не скасувати.

**S-P23.** `SaveAsync` мутує домен-об'єкт викликача (`Meta.UpdatedAt`, `IsDirty`) — побічні ефекти в шарі сховища.

**S-P24.** `MaxBackups`/`.backups` захардкоджені (19-20) — у застосунку вже є `ISettingsService`.

---

## 3. FamilyTree.App (WPF)

### Локалізація — перевірено, розбіжностей немає

Скрипт через `xml.etree` по обох `.resx`: **139 ключів в `uk`, 139 в `en`; відсутніх в одному з файлів — 0; дублікатів `name` — 0; неперекладених (ідентичні значення) — 0; плейсхолдери `{0}/{1}/{2}` збігаються в усіх 13 форматованих ключах.** Ключів, які код запитує, а resx не має, теж немає (перевірено `GetString("…")`, `{loc:Localize}`, `nameKey`-літерали, `TitleKey`/`ConfirmKey`, `ValidationKeys`).

Хардкоджених користувацьких рядків немає: 6 Cyrillic-літералів у `.cs` — це Serilog-логи (`App.xaml.cs:45,92,138,168,176,183`) + одне dev-повідомлення (`LocalizationSource.cs:27`); у XAML нелокалізовані лише мовно-нейтральні `1×`…`5×` і `100%`. `App.FallbackError()` (230-231) — задокументований останній рубіж.

Живі проблеми локалізації — A-B8, A-D15, A-D16, A-D17, A-D25 нижче.

### Критично

**A-C1. `ViewModels/MainViewModel.cs:713-722` + `MainWindow.xaml:129` — `Persons.Clear()` скидає виділення → подвійна повна перебудова дерева.**
```csharp
Persons.Clear();
foreach (var person in ordered) { Persons.Add(person); }
if (selectedId is { } id) SelectedPerson = Persons.FirstOrDefault(p => p.Id == id);
```
`SelectedItem="{Binding SelectedPerson}"` — TwoWay за замовчуванням. `Clear()` піднімає `Reset` → ListBox синхронно пише `null` у VM → `OnSelectedPersonChanged(null)` → `_tree.SetRoot(null)` → `Rebuild()`; потім елементи додаються; потім `SelectedPerson` повертається → **другий повний `Rebuild()`**. Кожна зміна вмісту / сортування / кожен debounce-пошук = ≥2 повні перерахунки розкладки + N розрахунків родства, плюс скидання стану вкладки «Дерево».
*Фікс:* `_suppressSelectionSync` навколо `RefreshPersons()`; краще — `ICollectionView` з `Filter`/`SortDescriptions` над однією стабільною колекцією (виділення не втрачається, контейнери не перебудовуються). Заодно закриває A-B14.

**A-C2. `ViewModels/TreeViewModel.cs:169-317`, зокрема `:212` — O(N) розрахунків родства в UI-потоці.**
```csharp
RelationBadge = isRoot ? youBadge
    : _kinship.Compute(rootPerson, person, graph, includeAffinity: true).DisplayName,
```
`Compute` = `FindNearestSet` (2 BFS), а коли кровного зв'язку немає (типово в «Усі родичі») — ще `TryAffinity` з `FindNearestSet` **на кожного з подружжя обох осіб** (+2k BFS). `Rebuild()` ≈ O(N × (V+E)) синхронно. На `samples/rodyna-500.familytree` (500 осіб, 528 parent-links, 235 spouse-links) при глибині 0 — секунди фризу, і за A-C1 це відбувається двічі на дію. Ні `IsBusy`, ні прогресу, ні скасування.
*Фікс:* кеш бейджів `Dictionary<Guid,string>` з ключем `(rootId, style, lang)`; для >~150 вузлів — `Task.Run` + застосування через `Dispatcher` з `IsBusy`; `AncestorDistances(root)` рахувати один раз (див. D-B6).

**A-C3. `ViewModels/TreeViewModel.cs:165,167` — візуальні перемикачі запускають повну перебудову з родством.**
```csharp
partial void OnShowGenerationBandsChanged(bool value) => Rebuild();
partial void OnFlipVerticalChanged(bool value) => Rebuild();
```
«Смуги поколінь» і «Предки знизу» не змінюють ні склад вузлів, ні назви родства — лише координати/фон, але кожен клік проганяє `Build` + N × `Compute`. На 500 особах чекбокс підвисає на секунди.
*Фікс:* окремий `RelayoutOnly()` з кешованої `TreeLayout`.

**A-C4. `App.xaml.cs:136-143` — `async void OnExit`: лог і Dispose можуть не виконатися.**
```csharp
protected override async void OnExit(ExitEventArgs e)
{
    Log.Information(...);
    await _host.StopAsync();
    _host.Dispose(); AppLog.Shutdown(); base.OnExit(e);
}
```
WPF не очікує `OnExit`. Після await метод повертає керування, `Application.Run` завершується, диспетчер вимикається — продовження може не отримати шансу. Наслідки: Serilog не робить `CloseAndFlush()` → **останні записи журналу губляться** (а саме на них розраховує `Error_Unexpected_Message`, який показує користувачу шлях до логу); `MainViewModel.Dispose()` (єдине місце, де знімаються підписки) теж не викликається.
*Фікс:* синхронний `OnExit` з `StopAsync().GetAwaiter().GetResult()` + таймаут, або stop у `MainWindow.Closed`.

**A-C5. `Styles/Controls.xaml:140-166` — неявний стиль `ComboBox` без `BasedOn` вбиває віртуалізацію.**
Немає `BasedOn="{StaticResource {x:Type ComboBox}}"` → губляться сеттери типової теми: `VirtualizingStackPanel` як `ItemsPanel`, `ScrollViewer.CanContentScroll="True"`, `IsVirtualizing="True"`. Панель відкатується на невіртуалізований `StackPanel`. У «Хто кому» (`WhoIsWhoControl.xaml:15-22`) обидва комбобокси мають `ItemsSource="{Binding Persons}"` — **усі 500 осіб** реалізуються одразу при першому розкритті. Плюс немає `x:Name="PART_Popup"`, на який спирається клавіатурна навігація.

### Важливо

**A-B6. `Controls/FamilyGraphSurface.xaml:114-165` — тултіп будується нетерпляче для кожного вузла; Canvas не віртуалізується.**
`Border.ToolTip` містить **екземпляр елемента**, а не шаблон → весь візуал (Border, Grid, Image 92×112, 9 TextBlock з конвертерами ≈20 елементів) створюється разом із карткою. `ItemsPanel` усіх чотирьох `ItemsControl` — `<Canvas/>`, який не віртуалізує принципово. 500 вузлів ≈ 10 000 зайвих візуалів і довгий перший рендер.
*Фікс:* `ToolTipService.ToolTip` + `ContentTemplate` (лінива побудова) або `DataTemplate` у `Window.Resources`; культування вузлів за видимим `Rect` (`Scroller.HorizontalOffset/ViewportWidth`).

**A-B7. `Controls/TreeCanvasControl.xaml.cs:228-239` + `TreeCanvasControl.xaml:18-25` — бюджет пам'яті для експорту PNG не гарантований.**
```csharp
const long maxPixels = 40_000_000;
var maxScale = Math.Sqrt(maxPixels / (size.Width * size.Height));
var scale = Math.Clamp(Math.Min(requestedScale, maxScale), 0.25, requestedScale);
```
1. **Нижня межа `Clamp` перебиває бюджет:** якщо `maxScale < 0.25`, `Clamp(0.1, 0.25, requested)` = `0.25` — бюджет свідомо перевищується саме тоді, коли захист потрібен.
2. 40 Мпкс × 4 байти = 160 МБ непере­рваного unmanaged-буфера `Pbgra32` + копія в `PngBitmapEncoder` → пік ~330 МБ. Полотно ~24 000 × 1 700 падає з `COMException`/`OutOfMemoryException` через обмеження програмного растеризатора.
3. **Комент розходиться з XAML:** у коді «1×/2×/3×», у комбобоксі є `4×` і `5×` → реальний `requestedScale` до 5.0, ризик утричі вищий за задокументований.
*Фікс:* `scale = Math.Min(requestedScale, maxScale)` + окреме попередження при `scale < 0.5`; обмежити виміри (≤16 000 px на сторону); ловити `OutOfMemoryException`/`COMException` і пропонувати менший масштаб; синхронізувати комент.

**A-B8. `MainWindow.xaml:109-111` + `Styles/Controls.xaml:56-85` — локалізована підказка пошуку ніколи не показується.**
```xml
<TextBox ... Tag="{loc:Localize PersonList_SearchHint}" />
```
У шаблоні `InputTextBoxStyle` є лише `<ScrollViewer x:Name="PART_ContentHost" .../>` — ні водяного знака, ні тригера на порожній `Text`. `Tag` сам по собі нічого не малює. Ключ `PersonList_SearchHint` перекладено обома мовами, але користувач його **ніколи не бачить**. Те саме в `RelationshipEditorWindow.xaml:18-19` (там навіть `Tag` немає).
*Фікс:* додати в шаблон `TextBlock` з `Text="{TemplateBinding Tag}"`, `IsHitTestVisible="False"` + тригер `<Trigger Property="Text" Value=""/>`.

**A-B9. `ViewModels/PersonEditorViewModel.cs:15-34` + `PersonEditorWindow.xaml:26,30,44,79` — помилки валідації не показуються і не локалізовані.**
`[Required]` без `ErrorMessage` генерує вбудований англійський `"The LastName field is required."` — не з resx. І він усе одно ніде не виводиться: у `Controls.xaml` немає жодного `Validation.ErrorTemplate` (grep — 0 входжень `Validation`). Користувач бачить лише червону рамку й **вимкнену кнопку «Зберегти» без пояснення**. Особливо болісно з `SelectedGender`: у режимі створення `null`, ComboBox виглядає нормально, Save мовчки заблокований.
*Фікс:* ключі `Validation_Required_LastName/_FirstName/_Gender` в обидва resx + власний `ValidationAttribute`, що резолвить ключ через `ILocalizationService`; спільний `Validation.ErrorTemplate` з `ToolTip="{Binding (Validation.Errors)[0].ErrorContent}"`.

**A-B10. `ViewModels/PersonEditorViewModel.cs:121-123` — `ValidatePerson` не викликається нізвідки.**
Домен має перевірку й ключ `Validation_DeathBeforeBirth`, перекладений обома мовами. Grep: єдині входження — `ValidationKeys.cs`, `RelationshipValidator.cs`, тест. Можна зберегти особу з датою смерті 1900 і народження 1990. Аналогічно `RelationshipEditorViewModel` не перевіряє `DivorceDate >= MarriageDate` (домен такої перевірки взагалі не має).
*Фікс:* виклик `RelationshipValidator.ValidatePerson` у `Commit()`, прогнати через наявний `Accept(result)` («продовжити?»); додати `Validation_DivorceBeforeMarriage`.

**A-B11. `Settings/SettingsService.cs:55-69` — `Save()` без try/catch, викликається з сеттерів прив'язки.**
`Load()` захищено, `Save()` — ні, хоч він викликається з `OnSelectedLanguageChanged/OnSelectedThemeChanged/OnSelectedNamingStyleChanged` (636, 648, 659) і `SettingsViewModel` (89, 101, 113, 120) — тобто всередині write-back прив'язки `SelectedItem="{Binding …}"`. Якщо AppData недоступний, виняток летить із сеттера, WPF **проглинає його як binding error**, і мова перемикається, але не зберігається — мовчки. А в шляху `AddRecent → _settings.Save()` (796) виняток долетить до `WriteAsync`-catch і покаже «Помилка файлу» з текстом про `settings.json`, хоч файл дерева збережено успішно.

**A-B12. `ViewModels/MainViewModel.cs:605-607` — `string.Format` над користувацьким перекладом може повалити застосунок.**
```csharp
string.Format(_localization.GetString(m.Key), m.Arguments.ToArray())
```
`GetString` (`LocalizationService.cs:64-76`) спершу шукає в `_customStrings` — JSON із `%AppData%\FamilyTree\languages\<code>.json`, який редагує сам користувач (задокументована фіча, `ADD-LANGUAGE.md`). Описка `{0` або зайвий `{2}` → `FormatException` → `DispatcherUnhandledException` → «Неочікувана помилка». Так само `PersonsCountText` (150), `Import_Confirm` (250), `Person_Delete_Confirm` (447), `Demo_Done` (301), `Tree_Card_MarriedSince` (`TreeViewModel.cs:441`), `Export_Done` (`TreeCanvasControl.xaml.cs:172`) — 13 форматованих ключів. Завантаження JSON захищено try/catch (134), а *використання* — ні.
*Фікс:* `LocalizationService.Format(key, args)` з `catch (FormatException)` → лог + відкат на нейтральний resx; валідувати плейсхолдери при завантаженні кастомної мови.

**A-B13. `ViewModels/MainViewModel.cs:362-372` — `FilePath` призначається до успішного запису.**
```csharp
_session.FilePath = path;   // ← до запису
return await WriteAsync(path);
```
Після невдалого «Зберегти як» сесія вказує на неіснуючий файл → наступний Ctrl+S піде через `SaveInternalAsync` (`FilePath` не пустий) → **безшумний запис у той самий проблемний шлях без діалогу**. Заголовок уже показує нову назву.

**A-B14. `ViewModels/MainViewModel.cs:719-722` — пошук фільтрує → виділення й дерево зникають.**
Якщо особа не проходить фільтр, `FirstOrDefault` дає `null` → `SelectedPerson = null` → вкладка «Особа» показує вітальний екран, вкладка «Дерево» повністю очищається (`SetRoot(null)`). Після очищення пошуку виділення **не відновлюється** (`selectedId` уже `null`).
*Фікс:* окреме поле `_lastSelectedId` незалежно від фільтра; природно вирішується разом з A-C1.

### Дрібне

**A-D15. `Localization/LocalizationService.cs:157-163` — `DatePicker` завжди en-US.**
`Apply` виставляє `CurrentCulture`/`CurrentUICulture`, але WPF форматує й **парсить** дати за `FrameworkElement.LanguageProperty`, чиє значення за замовчуванням жорстко `"en-US"`. Grep: ні `OverrideMetadata`, ні `xml:lang` у проєкті (0 входжень). Чотири `DatePicker` (`PersonEditorWindow.xaml:53,64`, `RelationshipEditorWindow.xaml:39,46`) показують `1/1/1980`, тоді як список осіб і картки дерева форматують те саме як `01.01.1980` (`TreeViewModel.FormatDate`, 369-370) — одна дата, два формати в одному вікні.
*Фікс:* у статичному ctor `App`:
```csharp
FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
    new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));
```
+ оновлювати `Language` активних вікон у `LanguageChanged`.

**A-D16. `Converters/DepthLabelConverter.cs:10-13` — підпис «Усі» не оновлюється при зміні мови.**
Конвертер читає `LocalizationSource.Instance["Tree_AllDepths"]`, але джерело прив'язки — `int`, який ніколи не піднімає `PropertyChanged` → конвертер не перезапускається. Відтворюється реально: `SettingsWindow` — модальний діалог, у якому **можна** змінити мову, і його ж комбобокс глибини не переклався (`TreeCanvasControl.xaml:47`, `SettingsWindow.xaml:53`).
*Фікс:* `IReadOnlyList<DepthOption : LocalizedOption>` (як уже зроблено для `TreeModeOption`/`PersonSortOption`) або `MultiBinding` з `LocalizationSource.Instance`.

**A-D17. `PersonEditorWindow.xaml:5,47`, `RelationshipEditorWindow.xaml:5,53` — заголовки/підписи діалогів не переперекладаються.**
`Title="{Binding TitleKey, Converter={StaticResource LocalizeConverter}}"` — джерело незмінне й не сповіщає. Вплив малий (вікна модальні, перемикача мови в них немає), але шаблон крихкий. Для порівняння: `SettingsWindow`/`DemoFamilyWindow` використовують `{loc:Localize}` і оновлюються коректно.
*Фікс:* `GenderOption` успадкувати від `LocalizedOption`; для заголовків — `MultiBinding` або `OnPropertyChanged(nameof(TitleKey))` з підписки.

**A-D18. `ViewModels/MainViewModel.cs:665-683` — `async void DebounceSearch`, CTS ніколи не Dispose.**
(а) виняток із `RefreshPersons()` летить прямо в `DispatcherUnhandledException`; (б) на кожне натискання клавіші створюється `CancellationTokenSource` із зареєстрованим таймером, старий не вивільняється до GC; (в) `_searchCts` не диспоузиться і в `Dispose()` (836-841).

**A-D19. `TreeViewModel.cs:63-70`, `WhoIsWhoViewModel.cs:38-40` — підписки лямбдами без можливості відписатися.**
Практичного leak немає — і VM, і сервіси — `Singleton` (`App.xaml.cs:57-84`). Але це асиметрія з `MainViewModel`, який реалізує `IDisposable` і знімає ті самі три підписки (836-841). Анонімні лямбди відписати неможливо в принципі → при переході на `Transient`/`Scoped` (мультидокументний режим) кожен екземпляр залишиться живим назавжди.

**A-D20. `Localization/LocalizedOption.cs:12-16` — підписка на singleton без відписки.**
Кожен `LocalizedOption` прив'язується до `LocalizationSource.Instance` назавжди. Зараз leak обмежений: усі похідні — `static readonly` (~13 екземплярів). Але клас `public abstract`, і створення в циклі дасть справжній необмежений leak. Weak-event не використано.

**A-D21. `ViewModels/MainViewModel.cs:306-317` — `Detach()` не в `finally`.**
Якщо `ShowSettings` кине виняток (напр. `SettingsService.Save()` з A-B11 усередині діалогу), `SettingsViewModel` залишиться підписаним на `LanguageChanged` назавжди; кожне повторне відкриття додає ще одного «зомбі».

**A-D22. `ViewModels/MainViewModel.cs:407-433` — `MarkContentChanged()` навіть без реальних змін.**
`PersonEditorViewModel.Commit()` завжди виставляє `person.UpdatedAt = DateTime.UtcNow` (125) і повертає ненульовий результат → натискання «Зберегти» без правок помічає документ брудним (`*` у заголовку, зайвий запит при закритті) і запускає ланцюжок з A-C1.

**A-D23. `ViewModels/MainViewModel.cs:814-824` — потрійне надлишкове оновлення на одну мутацію.**
```csharp
private void OnContentChanged(...) { RefreshPersons(); RefreshRelations(); }
```
`RefreshPersons` сам викликає `RefreshRelations` 2× через churn виділення (A-C1), потім ще раз явно. Плюс `AddPerson` (413-415) робить `MarkContentChanged()` → `RefreshPersons`, а тоді `SelectById(created.Id)` (732-736) → **знову** `RefreshPersons`. Одне додавання особи = 2-3 `RefreshPersons` і 3-4 `RefreshRelations`, кожен із перебудовою `doc.Persons.ToDictionary` (750) і `Rebuild()`.

**A-D24. `TreeViewModel.cs:191`, `MainViewModel.cs:750`, `WhoIsWhoViewModel.cs:94` — `ToDictionary(p => p.Id)` падає на дублікатах Id.**
Три різні місця + `FamilyGraph` ctor. Повідомлення користувачу — «Неочікувана помилка», а не «файл пошкоджено». Див. S-C3.
*Фікс:* перевірка унікальності в `DocumentMapper.ToDomain` з `InvalidDataException` (його вже коректно піймає `catch` в `OpenPathAsync`, 398-402) + `DistinctBy` у VM як другий рівень захисту.

**A-D25. Два «мертвих» resx-ключі.**
З 26 кандидатів 24 — хибні спрацювання grep (`Export_*` через локальну `L(...)` у `TreeCanvasControl.xaml.cs:137`; `Validation_*` через `ValidationKeys`; `Rel_Add*` через `TitleKey`; `Tree_Card_*` через `Line(...)`; `Relation_Remove*_Confirm` через `ConfirmRemoveRelation`; `Tree_AllDepths` через `DepthLabelConverter`). Реально невикористані:
- `Common_New` = «Нова родина» (для кнопки «Новий» використовується `Menu_New`);
- `StatusBar_NoFile` = «Файл не відкрито» (`DocumentName`, 154-167, віддає `Doc_Untitled`).

**A-D26. `MainWindow.xaml.cs:17-33` — `OnClosing` допускає повторний вхід і кличе `base` до рішення.**
(а) поки виконується `await` (MessageBox + можливо «Зберегти як»), користувач може ще раз натиснути Alt+F4 — `_forceClose` усе ще `false` → **другий** запит «Незбережені зміни»; (б) `base.OnClosing(e)` викликається до вирішення, тож зовнішні підписники бачать `e.Cancel == false`; (в) прямий виклик `PromptSaveIfDirtyAsync` обходить захист від паралельного запуску, вбудований у `AsyncRelayCommand` (яким решта файлових команд захищена коректно).
*Фікс:* `private bool _closing` guard, `base.OnClosing(e)` в кінець, скидання в `finally`.

**A-D27. `FamilyGraphSurface.xaml:45,82,179`, `TreeViewModel.cs:321-325` — жорстко закодовані кольори не реагують на тему.**
`Background="#22D64545" BorderBrush="#D64545"` (рамка подружжя), `Stroke="#FF8C00"` (підсвітка ребра), `BandPalette = { "#1A3A8FD6", "#3A2F72B0" }`. Решта проєкту послідовно ходить через `DynamicResource`. Для смуг це задокументоване рішення (напівпрозорі), для червоної рамки й оранжевої підсвітки — ні: у темній темі контраст `#22D64545` помітно гірший.
*Фікс:* `CoupleBoxBrush`, `CoupleBorderBrush`, `HighlightBrush` у `Theme.Light/Dark.xaml`.

**A-D28. `DemoFamilyWindow.xaml:84-85` — поле «Насіння» без валідації.**
`Text="{Binding Seed}"`, `Seed` — `int`. Введення `abc` → помилка конвертації, яку WPF проглинає: у VM лишається старе значення, поле показує невалідний текст, генерація тихо йде зі старим насінням. Ні `ValidatesOnExceptions`, ні `ErrorTemplate`, ні `PreviewTextInput`-фільтра.

### Покращення

**A-P29. `App.xaml.cs:90-134` — `async void OnStartup` без обробки помилок ініціалізації.**
Після `await _host.StartAsync()` виняток у кроках 1-6 (напр. `LoadCustomLanguages` із битою текою, `theme.SetTheme` із зіпсованим `Theme.Dark.xaml`) піде в `OnDispatcherUnhandledException`, який ставить `e.Handled = true` (170) → **застосунок продовжить роботу в напівініціалізованому стані** (без вікна, без `LocalizationSource`).
*Фікс:* try/catch у `OnStartup` із `Shutdown(1)`, або не гасити винятки до `mainWindow.Show()`.

**A-P30. `App.xaml.cs:51-52` — статичний `App.Services` як service locator.**
`Controls/TreeCanvasControl.xaml.cs:136` — `App.Services.GetService<ILocalizationService>()`, єдине місце, де DI обходиться, і воно ж робить контрол нетестовним. Контрол уже отримує `TreeViewModel` через `DataContext` → логічніше винести експорт у `TreeViewModel.ExportPngCommand` з інжектованими `ILocalizationService` + `IDialogService`; заодно піде з code-behind робота з файлами й `MessageBox` (141, 171, 177), яка порушує MVVM решти проєкту.

**A-P31. `MainViewModel.cs:131-136`, `SettingsViewModel.cs:71,73`, `TreeViewModel.cs:76` — `.ToList()` на кожне читання властивості.**
Джерела незмінні, а `LocalizedOption.DisplayName` сам сповіщає про зміну мови → `OnPropertyChanged` у `OnLanguageChanged` (829-831) уже не потрібен. `ToList()` лише алокує й змушує кожен ComboBox перебудувати контейнери (з ризиком скидання `SelectedItem`).

**A-P32. `Services/DialogService.cs:32,36,39` — `ActiveWindow!` із форсованим `null`.**
`ActiveWindow` (71-73) може бути `null` (жодне вікно не активне, `MainWindow` ще не призначено — реально досяжно, бо `OpenFileAsync` кличеться з `App.OnStartup:130` одразу після `Show()`). `MessageBox.Show(null, ...)` кине `ArgumentNullException`. *Фікс:* перевантаження без owner, коли `ActiveWindow is null`.

**A-P33. `Services/DocumentSession.cs:21-26` — `NewDocument` не скидає `IsDirty` явно.**
Працює лише тому, що `FamilyDocument.CreateNew` віддає `IsDirty == false` за замовчуванням, тоді як `SetDocument` (28-35) виставляє явно. Асиметрія крихка.

### Перевірено й справне (щоб не шукати вдруге)

- **resx-синхронність**: 139/139, 0 розбіжностей (див. вище).
- **Dirty-state**: `New` (189), `Open` (198), `OpenRecent` (218), `CreateDemoFamily` (279), `OpenFileAsync` (329) — усі кличуть `PromptSaveIfDirtyAsync()`; `MainWindow.OnClosing` обробляється; `MarkContentChanged` присутній у всіх мутаціях (`AddPerson`, `EditPerson`, `DeletePerson`, `AddParent`, `AddChild`, `AddSpouse`, `RemoveParent`, `RemoveChild`, `EditSpouse`, `RemoveSpouse`, `Import`, демо). `Import` свідомо не питає (адитивний) — коректно.
- **Захист від повторного входу**: `[RelayCommand]` над `async Task` → `AsyncRelayCommand`, який у CommunityToolkit 8.4 забороняє конкурентне виконання через `CanExecute`; `Save`/`Open`/`New`/`Import` захищені, включно з `KeyBinding` (`MainWindow.xaml:11-15`). Єдиний обхід — A-D26.
- **`RenderSceneToBitmap`**: окремий off-screen `FamilyGraphSurface` замість `VisualBrush` над екранним — правильний підхід; `FileStream` в `using` (166-169), `bitmap.Freeze()` (238).
- **Віртуалізація `ListBox`** осіб (`MainWindow.xaml:129`) працює — власного стилю для `ListBox` немає, діє типовий `VirtualizingStackPanel`.
- **Порядок ініціалізації в `App.OnStartup`** коректний: `LocalizationSource.Initialize` (крок 2) до першої резолюції `IThemeService` (крок 3), чий ctor створює `ThemeOption : LocalizedOption`. Тонке місце, комент (103-104) його фіксує.
- **`TitleBarThemer.Track`** (42-71) — приклад коректної роботи з подіями: підписка на `ThemeChanged` знімається в `window.Closed`, `SourceInitialized` знімає себе. `DialogService.ShowDialog` (64-69) застосовує до всіх діалогів.
- **`LocalizationService`** стійкий до битих даних: невідомий код мови → відкат на `uk` (146-155), битий JSON не ламає старт (134), відсутній ключ → `[Key]` (75), неіснуюча `CultureInfo` → запасна (166-181).
- **Culture-sensitivity сховища** — див. S-P21.

---

## Прогалини в тестах

### Domain
**Родство:** нерідні батьки/діти (D-C1); особа водночас кровний родич і подружжя (D-C2); свояцтво через *розлучене* подружжя (`FormerSpouseTests` перевіряє лише пряме, D-B3); `Gender.Unknown` — лише один випадок (`KinshipTests.cs:96`), немає невідомої статі для подружжя/свояка/сполучної особи X (D-D11); напіврідні при неповних даних (D-B4); неоднозначний НСП, `(1,3)` vs `(3,1)`, стабільність `AncestorIds[0]` (D-D13); цикл A→B→A і самозв'язок на рівні графа (D-D16); `ParentRole.Adoptive/Step` не зафіксовано тестом; далекі кузени лише до `depth: 5` — fallback «8-юрідний»/«21th» не перевіряється (D-D26); англійський `Detailed` стиль і англійське `half-` для невідомої статі.

**Граф:** дублікати `Person.Id` (зараз → `ArgumentException`), дублікати `ParentChildLink`/`SpouseLink`, ненормалізований `SpouseLink` (D-B9, D-D15); повторний шлюб + `IsSpouseActive` (D-D14).

**Розкладка:** кілька шлюбів (найважливіша дірка — D-B5); ізольований корінь як єдиний вузол; `FullRelatives` з `maxDepth`; детермінізм (той самий вхід → та сама розкладка); відсутність втрати осіб, коли дитина належить двом парам.

**Валідація:** `ValidatePerson` через жоден сценарій (D-B10); другий біологічний батько зі статтю `Unknown` (D-B8); неіснуючі особи в кандидаті; дати шлюбу/розлучення (правил немає, D-P27).

**Генератор і масштаб:** `BirthDate <= today` (D-D20); не більше одного чинного шлюбу (D-D21); смерть після народження дітей (D-D24); точне дотримання `MaxPersons` (D-D22). Немає жодного тесту на масштаб, хоч ТЗ орієнтується на 5000 осіб — «граф на 2000 осіб + `Compute` для всіх вузлів укладається в N мс» зафіксував би D-B6 і D-B7.

### Storage
Битий JSON; `"meta": null` / `"persons": null`; dangling links; дубльовані та відсутні `id`; `schemaVersion` як рядок / 0 / негативний; фейкова міграція 1→2; паралельні `SaveAsync`; відновлення з бекапу; кирилиця без escape-послідовностей у збереженому файлі.
