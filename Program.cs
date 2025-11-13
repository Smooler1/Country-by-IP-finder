using Microsoft.Data.Sqlite;
using System;
using System.IO;
using System.Net;
using System.Numerics;
using System.Runtime.Serialization;

namespace Country_by_IP_finder
{
    class Program
    {
        static void Main()
        {
            string csvPath = "IPDB/geo-US.csv";

            using var connection = new SqliteConnection("Data Source=ipdb.sqlite");
            connection.Open();

            var createTableCmd = connection.CreateCommand();
            createTableCmd.CommandText =
            @"
                CREATE TABLE IF NOT EXISTS ip_ranges (
                    network TEXT,
                    country_code TEXT,
                    country_name TEXT,
                    state_code TEXT,
                    state_name TEXT,
                    ip_start TEXT,
                    ip_end TEXT
                );
            ";
            createTableCmd.ExecuteNonQuery();

            var clearCmd = connection.CreateCommand();
            clearCmd.CommandText = "DELETE FROM ip_ranges;";
            clearCmd.ExecuteNonQuery();

            using var transaction = connection.BeginTransaction();

            var insertCmd = connection.CreateCommand();
            insertCmd.CommandText = @"
                INSERT INTO ip_ranges
                (network, country_code, country_name, state_code, state_name, ip_start, ip_end)
                VALUES (@network,@cc,@cn,@sc,@sn,@start,@end);
            ";

            var networkParam = insertCmd.CreateParameter(); networkParam.ParameterName = "@network"; insertCmd.Parameters.Add(networkParam);
            var ccParam = insertCmd.CreateParameter(); ccParam.ParameterName = "@cc"; insertCmd.Parameters.Add(ccParam);
            var cnParam = insertCmd.CreateParameter(); cnParam.ParameterName = "@cn"; insertCmd.Parameters.Add(cnParam);
            var scParam = insertCmd.CreateParameter(); scParam.ParameterName = "@sc"; insertCmd.Parameters.Add(scParam);
            var snParam = insertCmd.CreateParameter(); snParam.ParameterName = "@sn"; insertCmd.Parameters.Add(snParam);
            var startParam = insertCmd.CreateParameter(); startParam.ParameterName = "@start"; insertCmd.Parameters.Add(startParam);
            var endParam = insertCmd.CreateParameter(); endParam.ParameterName = "@end"; insertCmd.Parameters.Add(endParam);

            int counter = 0;

            foreach (var line in File.ReadLines(csvPath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("network")) continue;

                var parts = line.Split(',');

                networkParam.Value = parts[0];
                ccParam.Value = parts[3];
                cnParam.Value = parts[4];
                scParam.Value = parts[5];
                snParam.Value = parts[6];

                var (start, end) = GetIpRange(parts[0]);
                startParam.Value = start.ToString();
                endParam.Value = end.ToString();

                insertCmd.ExecuteNonQuery();
                counter++;
                if (counter % 10000 == 0) Console.WriteLine($"{counter} строк вставлено...");
            }

            transaction.Commit();
            Console.WriteLine($"CSV успешно загружен в базу! Всего строк: {counter}\n");

            Console.WriteLine("Введите IP для поиска (IPv4 или IPv6), или напишите 'exit' для выхода:");

            while (true)
            {
                Console.Write("> ");
                string inputIp = Console.ReadLine()?.Trim();

                if (string.IsNullOrWhiteSpace(inputIp))
                {
                    Console.WriteLine("Введите IP или 'exit'.");
                    continue;
                }

                if (inputIp.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Выход из программы...");
                    break;
                }

                if (inputIp.Contains("/"))
                {
                    Console.WriteLine("Похоже, вы ввели подсеть. Введите один конкретный IP.");
                    continue;
                }

                try
                {
                    BigInteger ipNum = IpToBigInt(inputIp);

                    var searchCmd = connection.CreateCommand();
                    searchCmd.CommandText = @"
            SELECT country_code, country_name, state_code, state_name
            FROM ip_ranges
            WHERE CAST(ip_start AS TEXT) <= @ip AND CAST(ip_end AS TEXT) >= @ip
            LIMIT 1;
        ";
                    searchCmd.Parameters.AddWithValue("@ip", ipNum.ToString());

                    using var reader = searchCmd.ExecuteReader();
                    if (reader.Read())
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"Страна: {reader.GetString(1)} ({reader.GetString(0)})");
                        Console.WriteLine($"Штат: {reader.GetString(3)} ({reader.GetString(2)})");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.WriteLine("IP не найден в базе.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Ошибка: {ex.Message}");
                }
            }
        }

        static BigInteger IpToBigInt(string ip)
        {
            if (ip.Contains(":"))
            {
                if (IPAddress.TryParse(ip, out var ipAddr))
                {
                    byte[] bytes = ipAddr.GetAddressBytes();
                    Array.Reverse(bytes); // для BigInteger
                    return new BigInteger(bytes);
                }
                else throw new Exception("Неправильный формат IPv6");
            }
            else
            {
                var parts = ip.Split('.');
                if (parts.Length != 4) throw new Exception("Неправильный формат IPv4");
                uint a = uint.Parse(parts[0]);
                uint b = uint.Parse(parts[1]);
                uint c = uint.Parse(parts[2]);
                uint d = uint.Parse(parts[3]);
                return a * 256u * 256u * 256u + b * 256u * 256u + c * 256u + d;
            }
        }

        static (BigInteger start, BigInteger end) GetIpRange(string cidr)
        {
            var parts = cidr.Split('/');
            var ip = IpToBigInt(parts[0]);
            int prefix = int.Parse(parts[1]);

            int totalBits = parts[0].Contains(":") ? 128 : 32;
            BigInteger mask = BigInteger.Pow(2, totalBits) - 1;
            mask <<= (totalBits - prefix);

            BigInteger start = ip & mask;
            BigInteger end = start | (~mask & (BigInteger.Pow(2, totalBits) - 1));
            return (start, end);
        }
    }
}
