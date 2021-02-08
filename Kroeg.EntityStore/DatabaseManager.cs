using System.Data.Common;
using System.Linq;
using Dapper;

namespace Kroeg.EntityStore
{
  public class DatabaseManager
  {
    private readonly DbConnection _connection;

    public DatabaseManager(DbConnection connection)
    {
      _connection = connection;
    }

    private class KroegMigrationEntry
    {
      public int Id { get; set; }
      public string Name { get; set; }
    }

    public void EnsureExists()
    {
      _connection.Execute("create table if not exists kroeg_migrations (\"Id\" serial primary key, \"Name\" text)");

      var migrations = _connection.Query<KroegMigrationEntry>("select * from kroeg_migrations");
      if (!migrations.Any())
      {
        CreateTables();
      }
    }

    private void CreateTables()
    {
      _connection.Execute(@"create table ""Attributes"" (
                ""AttributeId"" serial primary key,
                ""Uri"" text not null unique
            );");

      _connection.Execute(@"create index on ""Attributes""(""Uri"")");

      _connection.Execute(@"create table ""TripleEntities"" (
                ""EntityId"" serial primary key,
                ""IdId"" int references ""Attributes""(""AttributeId""),
                ""Type"" text,
                ""Updated"" timestamp,
                ""IsOwner"" boolean
            )");

      _connection.Execute(@"create index on ""TripleEntities""(""IdId"")");

      _connection.Execute(@"create table ""Triples"" (
                ""TripleId"" serial primary key,
                ""SubjectId"" int not null references ""Attributes""(""AttributeId""),
                ""SubjectEntityId"" int not null references ""TripleEntities""(""EntityId""),
                ""PredicateId"" int not null references ""Attributes""(""AttributeId""),
                ""AttributeId"" int references ""Attributes""(""AttributeId""),
                ""TypeId"" int references ""Attributes""(""AttributeId""),
                ""Object"" text
            );");

      _connection.Execute(@"create index on ""Triples""(""SubjectEntityId"")");

      _connection.Execute(@"create table ""CollectionItems"" (
                ""CollectionItemId"" serial primary key,
                ""CollectionId"" int not null references ""TripleEntities""(""EntityId""),
                ""ElementId"" int not null references ""TripleEntities""(""EntityId""),
                ""IsPublic"" boolean
            );");

      _connection.Execute(@"create index on ""CollectionItems""(""CollectionId"")");
      _connection.Execute(@"create index on ""CollectionItems""(""ElementId"")");

      _connection.Execute(@"create table ""UserActorPermissions"" (
                ""UserActorPermissionId"" serial primary key,
                ""UserId"" text not null,
                ""ActorId"" int not null references ""TripleEntities""(""EntityId""),
                ""IsAdmin"" boolean
            );");

      _connection.Execute(@"create table ""EventQueue"" (
                ""Id"" serial primary key,
                ""Added"" timestamp not null,
                ""NextAttempt"" timestamp not null,
                ""AttemptCount"" int not null,
                ""Action"" text not null,
                ""Data"" text not null
            );");

      _connection.Execute(@"create table ""SalmonKeys"" (
                ""SalmonKeyId"" serial primary key,
                ""EntityId"" int not null references ""TripleEntities""(""EntityId""),
                ""PrivateKey"" text not null
            );");

      _connection.Execute(@"create index on ""SalmonKeys""(""EntityId"")");

      _connection.Execute(@"create table ""WebsubSubscriptions"" (
                ""Id"" serial primary key,
                ""Expiry"" timestamp not null,
                ""Callback"" text not null,
                ""Secret"" text,
                ""UserId"" int not null references ""TripleEntities""(""EntityId"")
            );");

      _connection.Execute(@"create table ""WebSubClients"" (
                ""WebSubClientId"" serial primary key,
                ""ForUserId"" int not null references ""TripleEntities""(""EntityId""),
                ""TargetUserId"" int not null references ""TripleEntities""(""EntityId""),
                ""Topic"" text not null,
                ""Expiry"" timestamp not null,
                ""Secret"" text
            );");

      _connection.Execute(@"create table ""JsonWebKeys"" (
                ""Id"" text not null,
                ""OwnerId"" int not null references ""TripleEntities""(""EntityId""),
                ""SerializedData"" text not null,
                primary key(""Id"", ""OwnerId"")
            );");

      _connection.Execute(@"create table ""Users"" (
                ""Id"" text not null primary key,
                ""Username"" text,
                ""NormalisedUsername"" text,
                ""Email"" text,
                ""PasswordHash"" text
            );");

      _connection.Execute("insert into kroeg_migrations (\"Name\") values ('hello, world')");
    }
  }
}
