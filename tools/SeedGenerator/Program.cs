using System.Text.Json;

// Генератор демо-родин у форматі .familytree.
// Запуск із Visual Studio: постав SeedGenerator стартовим проєктом і натисни ▶ (F5),
// або з консолі: dotnet run --project tools/SeedGenerator
// Файли пишуться в теку samples поруч із FamilyTree.sln.
//
// Керування розміром: generations — кількість поколінь (глибина); maxPersons — стеля осіб.
// Змінюй ці числа у викликах нижче.

var outputDir = ResolveSamplesDir();

GenerateFile(generations: 4, maxPersons: 150, seed: 4, title: "Родина (4 покоління)", fileName: "rodyna-4pok.familytree");
GenerateFile(generations: 6, maxPersons: 600, seed: 6, title: "Родина (6 поколінь)", fileName: "rodyna-6pok.familytree");

Console.WriteLine("Готово.");

void GenerateFile(int generations, int maxPersons, int seed, string title, string fileName)
{
    const int generationGap = 25;    // середня різниця поколінь, років
    const int youngestBirthYear = 2010;
    var rnd = new Random(seed);
    var persons = new List<Dictionary<string, object?>>();
    var parentChildLinks = new List<Dictionary<string, object?>>();
    var spouseLinks = new List<Dictionary<string, object?>>();

    string[] maleNames = { "Іван", "Петро", "Микола", "Андрій", "Сергій", "Василь", "Олег", "Тарас", "Богдан", "Юрій", "Роман", "Дмитро", "Максим", "Степан", "Григорій" };
    string[] femaleNames = { "Марія", "Ольга", "Ганна", "Наталія", "Оксана", "Тетяна", "Ірина", "Софія", "Катерина", "Людмила", "Віра", "Олена", "Дарина", "Зоряна", "Галина" };
    string[] surnames = { "Коваленко", "Шевченко", "Ткаченко", "Бондаренко", "Кравченко", "Мельник", "Поліщук", "Савчук", "Іваненко", "Мороз", "Гончар", "Лисенко", "Марченко", "Панченко", "Гриценко" };

    const string createdAt = "2024-01-01T00:00:00Z";
    var baseYear = youngestBirthYear - (generations - 1) * generationGap;

    string NewId() => Guid.NewGuid().ToString();
    string Iso(int y, int m, int d) => $"{y:D4}-{m:D2}-{d:D2}";

    string Patronymic(string father, bool female)
    {
        var stem = father;
        if (stem[^1] is 'о' or 'ь' or 'а' or 'я' or 'й')
        {
            stem = stem[..^1];
        }

        return female ? stem + "івна" : stem + "ович";
    }

    Dictionary<string, object?> AddPerson(string surname, bool female, int birthYear, string? fatherFirst, string? maiden)
    {
        var first = female ? femaleNames[rnd.Next(femaleNames.Length)] : maleNames[rnd.Next(maleNames.Length)];
        var p = new Dictionary<string, object?>
        {
            ["id"] = NewId(),
            ["lastName"] = surname,
            ["firstName"] = first,
            ["gender"] = female ? "Female" : "Male",
            ["birthDate"] = Iso(birthYear, rnd.Next(1, 13), rnd.Next(1, 28)),
            ["createdAt"] = createdAt,
            ["updatedAt"] = createdAt,
        };
        if (fatherFirst is not null)
        {
            p["middleName"] = Patronymic(fatherFirst, female);
        }
        if (maiden is not null)
        {
            p["maidenName"] = maiden;
        }
        // Померлі — ті, хто прожив би вже понад ~85 років.
        if (birthYear + 85 < 2024)
        {
            p["deathDate"] = Iso(birthYear + rnd.Next(63, 90), rnd.Next(1, 13), rnd.Next(1, 28));
        }

        persons.Add(p);
        return p;
    }

    void Link(string parentId, string childId) => parentChildLinks.Add(new()
    {
        ["id"] = NewId(),
        ["parentId"] = parentId,
        ["childId"] = childId,
        ["parentRole"] = "Biological",
    });

    void Marry(string aId, string bId, int year) => spouseLinks.Add(new()
    {
        ["id"] = NewId(),
        ["person1Id"] = aId,
        ["person2Id"] = bId,
        ["marriageDate"] = Iso(year, rnd.Next(1, 13), rnd.Next(1, 28)),
    });

    // Черга подружніх пар: (fatherId, fatherFirst, motherId, childrenSurname, generation, parentsBirthYear)
    var queue = new Queue<(string FatherId, string FatherFirst, string MotherId, string Surname, int Generation, int BirthYear)>();

    var founderSurname = surnames[rnd.Next(surnames.Length)];
    var husband = AddPerson(founderSurname, female: false, baseYear, fatherFirst: null, maiden: null);
    var wife = AddPerson(founderSurname, female: true, baseYear + rnd.Next(-2, 3), fatherFirst: null, maiden: surnames[rnd.Next(surnames.Length)]);
    Marry((string)husband["id"]!, (string)wife["id"]!, baseYear + rnd.Next(22, 28));
    queue.Enqueue(((string)husband["id"]!, (string)husband["firstName"]!, (string)wife["id"]!, founderSurname, 0, baseYear));

    while (queue.Count > 0 && persons.Count < maxPersons)
    {
        var (fatherId, fatherFirst, motherId, surname, generation, parentsBy) = queue.Dequeue();
        if (generation >= generations - 1)
        {
            continue; // останнє покоління дітей не має
        }

        var childrenCount = rnd.Next(2, 5); // 2..4
        var childGeneration = generation + 1;

        for (var c = 0; c < childrenCount && persons.Count < maxPersons; c++)
        {
            var female = rnd.NextDouble() < 0.5;
            var childBirth = parentsBy + rnd.Next(24, 43); // батькам 24–42 роки
            var child = AddPerson(surname, female, childBirth, fatherFirst, maiden: null);
            var childId = (string)child["id"]!;
            Link(fatherId, childId);
            Link(motherId, childId);

            // Одружуємо лише тих, хто НЕ в останньому поколінні (щоб мали власних дітей).
            if (childGeneration < generations - 1 && persons.Count < maxPersons)
            {
                var marryYear = childBirth + rnd.Next(20, 30); // шлюб у 20–29 років
                if (female)
                {
                    var hSurname = surnames[rnd.Next(surnames.Length)];
                    var spouse = AddPerson(hSurname, female: false, childBirth + rnd.Next(-3, 4), fatherFirst: null, maiden: null);
                    var spouseId = (string)spouse["id"]!;
                    Marry(childId, spouseId, marryYear);
                    queue.Enqueue((spouseId, (string)spouse["firstName"]!, childId, hSurname, childGeneration, childBirth));
                }
                else
                {
                    var spouse = AddPerson(surname, female: true, childBirth + rnd.Next(-3, 4), fatherFirst: null, maiden: surnames[rnd.Next(surnames.Length)]);
                    var spouseId = (string)spouse["id"]!;
                    Marry(childId, spouseId, marryYear);
                    queue.Enqueue((childId, (string)child["firstName"]!, spouseId, surname, childGeneration, childBirth));
                }
            }
        }
    }

    var doc = new Dictionary<string, object?>
    {
        ["schemaVersion"] = 1,
        ["meta"] = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["createdAt"] = createdAt,
            ["updatedAt"] = createdAt,
            ["appVersion"] = "1.0.0",
        },
        ["persons"] = persons,
        ["parentChildLinks"] = parentChildLinks,
        ["spouseLinks"] = spouseLinks,
    };

    var path = Path.Combine(outputDir, fileName);
    File.WriteAllText(path, JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"{fileName}: {generations} поколінь, {persons.Count} осіб, {parentChildLinks.Count} зв'язків, {spouseLinks.Count} шлюбів → {path}");
}

static string ResolveSamplesDir()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "FamilyTree.sln")))
    {
        dir = dir.Parent;
    }

    var root = dir?.FullName ?? Directory.GetCurrentDirectory();
    var samples = Path.Combine(root, "samples");
    Directory.CreateDirectory(samples);
    return samples;
}
