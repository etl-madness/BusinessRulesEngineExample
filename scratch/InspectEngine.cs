using System;
using System.Reflection;
using System.Linq;

class Program
{
    static void Main()
    {
        var asm1 = Assembly.LoadFrom(@"c:\Users\U00001\source\repos\BusinessRulesEngineExample\bin\Debug\net10.0\EtlAnalytics.RulesEngine.dll");
        var asm2 = Assembly.LoadFrom(@"c:\Users\U00001\source\repos\BusinessRulesEngineExample\bin\Debug\net10.0\EtlAnalytics.RulesEngine.Dapper.dll");

        Console.WriteLine("=== EtlAnalytics.RulesEngine Types ===");
        foreach (var type in asm1.GetTypes())
        {
            Console.WriteLine($"Type: {type.FullName}");
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                if (m.DeclaringType == type)
                    Console.WriteLine($"   Method: {m.Name}");
            }
        }

        Console.WriteLine("\n=== EtlAnalytics.RulesEngine.Dapper Types ===");
        foreach (var type in asm2.GetTypes())
        {
            Console.WriteLine($"Type: {type.FullName}");
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            {
                if (m.DeclaringType == type)
                    Console.WriteLine($"   Method: {m.Name}");
            }
        }
    }
}
