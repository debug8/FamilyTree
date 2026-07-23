using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

// Генератор демо-родин у форматі .familytree (запуск: dotnet run generate-families.cs).
// Створює rodyna-100.familytree та rodyna-500.familytree у поточній теці.

GenerateFile(target: 100, seed: 100, title: "Родина (100 осіб)", fileName: "rodyna-100.familytree");
GenerateFile(target: 500, seed: 500, title: "Родина (500 осіб)", fileName: "rodyna-500.familytree");

void GenerateFile(int target, int seed, string title, string fileName)
{
    var rnd = new Random(seed);
    var persons = new List<Dictionary<string, object?>>();
    var parentChildLinks = new List<Dictionary<string, object?>>();
    var spouseLinks = new List<Dictionary<string, object?>>();

    string[] maleNames = { "Іван", "Петро", "Микола", "Андрій", "Сергій", "Василь", "Олег", "Тарас", "Богдан", "Юрій", "Роман", "Дмитро", "Максим", "Степан", "Григорій" };
    string[] femaleNames = { "Марія", "Ольга", "Ганна", "Наталія", "Оксана", "Тетяна", "Ірина", "Софія", "Катерина", "Людмила", "Віра", "Олена", "Дарина", "Зоряна", "Галина" };
    string[] surnames = { "Коваленко", "Шевченко", "Ткаченко", "Бондаренко", "Кравченко", "Мельник", "Поліщук", "Савчук", "Іваненко", "Мороз", "Гончар", "Лисенко", "Марченко", "Панченко", "Гриценко" };

    const string createdAt = "2024-01-01T00:00:00Z";

    string NewId() => Guid.NewGuid().ToString();
    string Iso(int y, int m, int d) => $"{y:D4}-{m:D2}-{d:D2}";

    string Patronymic(string father, bool female)
    {
        var stem = father;
        var last = stem[^1];
        if (last is 'о' or 'ь' or 'а' or 'я' or 'й')
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
        if (birthYear < 1945)
        {
            p["deathDate"] = Iso(birthYear + rnd.Next(62, 92), rnd.Next(1, 13), rnd.Next(1, 28));
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

    // Черга подружніх пар, готових мати дітей: (fatherId, fatherFirst, motherId, childrenSurname, parentsBirthYear)
    var queue = new Queue<(string FatherId, string FatherFirst, string MotherId, string Surname, int BirthYear)>();

    var founderSurname = surnames[rnd.Next(surnames.Length)];
    var husband = AddPerson(founderSurname, female: false, birthYear: 1900, fatherFirst: null, maiden: null);
    var wife = AddPerson(founderSurname, female: true, birthYear: 1902, fatherFirst: null, maiden: surnames[rnd.Next(surnames.Length)]);
    Marry((string)husband["id"]!, (string)wife["id"]!, 1922);
    queue.Enqueue(((string)husband["id"]!, (string)husband["firstName"]!, (string)wife["id"]!, founderSurname, 1902));

    while (persons.Count < target && queue.Count > 0)
    {
        var (fatherId, fatherFirst, motherId, surname, parentsBy) = queue.Dequeue();
        var childrenCount = rnd.Next(1, 6);
        var childBase = Math.Min(parentsBy + 24 + rnd.Next(0, 4), 2016);
        var marriedAny = false;

        for (var c = 0; c < childrenCount && persons.Count < target; c++)
        {
            var female = rnd.NextDouble() < 0.5;
            var childBy = Math.Min(childBase + rnd.Next(0, 4), 2018);
            var child = AddPerson(surname, female, childBy, fatherFirst, maiden: null);
            var childId = (string)child["id"]!;
            Link(fatherId, childId);
            Link(motherId, childId);

            var forceMarry = !marriedAny && c == childrenCount - 1;
            if (persons.Count < target && (rnd.NextDouble() < 0.85 || forceMarry))
            {
                var marryYear = childBy + rnd.Next(22, 28);
                if (female)
                {
                    // Чоловік «приходить» у родину зі своїм прізвищем; діти — його прізвища.
                    var hSurname = surnames[rnd.Next(surnames.Length)];
                    var spouse = AddPerson(hSurname, female: false, childBy + rnd.Next(-3, 4), fatherFirst: null, maiden: null);
                    var spouseId = (string)spouse["id"]!;
                    Marry(childId, spouseId, marryYear);
                    queue.Enqueue((spouseId, (string)spouse["firstName"]!, childId, hSurname, childBy));
                }
                else
                {
                    // Дружина бере прізвище чоловіка; діти — цього прізвища.
                    var spouse = AddPerson(surname, female: true, childBy + rnd.Next(-3, 4), fatherFirst: null, maiden: surnames[rnd.Next(surnames.Length)]);
                    var spouseId = (string)spouse["id"]!;
                    Marry(childId, spouseId, marryYear);
                    queue.Enqueue((childId, (string)child["firstName"]!, spouseId, surname, childBy));
                }

                marriedAny = true;
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

    var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(fileName, json);
    Console.WriteLine($"{fileName}: {persons.Count} осіб, {parentChildLinks.Count} зв'язків, {spouseLinks.Count} шлюбів → {Path.GetFullPath(fileName)}");
}
