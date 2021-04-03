using System;
using System.Data;
using System.Linq;
using Dapper;
using Npgsql;

namespace CreateDatabase
{
    class Program
    {
        static void Main(string[] args)
        {
            var connectionString = Environment.GetEnvironmentVariable("CONNECTION_STRING").Replace("Database=kroeg4", "Database=postgres");

            IDbConnection connection = new NpgsqlConnection(connectionString);
            var existsDatabase = connection.Query<string>($"SELECT DATNAME FROM pg_catalog.pg_database WHERE DATNAME = 'kroeg4'");
            System.Console.WriteLine(existsDatabase);
            if (!existsDatabase.Any())
            {
                connection.Execute("CREATE DATABASE \"kroeg4\" WITH OWNER = postgres ENCODING = 'UTF8' CONNECTION LIMIT = -1;");
            }
        }
    }
}
