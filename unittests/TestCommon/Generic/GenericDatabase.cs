﻿using System.Data.Common;
using System.Transactions;
using Dapper;
using FluentAssertions;
using grate.Configuration;
using grate.Migration;
using TestCommon.TestInfrastructure;
using Xunit.Abstractions;
using static System.StringSplitOptions;

namespace TestCommon.Generic;

public abstract class GenericDatabase(IGrateTestContext context, ITestOutputHelper testOutput)
{
    protected GenericDatabase(): this(null!, null!)
    {
    }

    protected IGrateTestContext Context { get; init; } = context;
    protected ITestOutputHelper TestOutput { get; init; } = testOutput;

    [Fact]
    public virtual async Task Is_up_and_running_with_appropriate_database_version()
    {
        string? res;
        using (var conn = Context.CreateAdminDbConnection())
        {
            conn.Open();
            var commandText = Context.Sql.SelectVersion;
            res = (string?)await conn.ExecuteScalarAsync(commandText);
        }

        res.Should().StartWith(Context.ExpectedVersionPrefix);
    }

    [Fact]
    public async Task Is_created_if_confed_and_it_does_not_exist()
    {
        var db = "NEWDATABASE";

        await using (var migrator = GetMigrator(GetConfiguration(db, true)))
        {
            await migrator.Migrate();
        }

        IEnumerable<string> databases = await GetDatabases();
        databases.Should().Contain(db);
    }

    [Fact]
    public virtual async Task Is_created_with_custom_script_if_custom_create_database_folder_exists()
    {
        var scriptedDatabase = "CUSTOMSCRIPTEDDATABASE";
        var confedDatabase = "DEFAULTDATABASE";

        var config = GetConfiguration(confedDatabase, true);
        var password = Context.AdminConnectionString
            .Split(";", TrimEntries | RemoveEmptyEntries)
            .SingleOrDefault(entry => entry.StartsWith("Password") || entry.StartsWith("Pwd"))?
            .Split("=", TrimEntries | RemoveEmptyEntries)
            .Last();

        var customScript = Context.Syntax.CreateDatabase(scriptedDatabase, password);
        TestConfig.WriteContent(Wrap(config.SqlFilesDirectory, config.Folders?.CreateDatabase?.Path), "createDatabase.sql", customScript);
        try
        {
            await using var migrator = GetMigrator(config);
            await migrator.Migrate();
        }
        catch (DbException)
        {
            //Do nothing because database name is wrong due to custom script
        }

        File.Delete(Path.Join(Wrap(config.SqlFilesDirectory, config.Folders?.CreateDatabase?.Path).ToString(), "createDatabase.sql"));

        // The database should have been created by the custom script
        IEnumerable<string> databases = (await GetDatabases()).ToList();
        databases.Should().Contain(scriptedDatabase);
        databases.Should().NotContain(confedDatabase);
    }

    [Fact]
    public async Task Is_not_created_if_not_confed()
    {
        var db = "SOMEOTHERDATABASE";

        IEnumerable<string> databasesBeforeMigration = await GetDatabases();
        databasesBeforeMigration.Should().NotContain(db);

        await using (var migrator = GetMigrator(GetConfiguration(db, false)))
        {
            // The migration should throw an error, as the database does not exist.
            if (ThrowOnMissingDatabase)
            {
                await Assert.ThrowsAsync(Context.DbExceptionType, () => migrator.Migrate());
            }
        }

        // Ensure that the database was in fact not created 
        IEnumerable<string> databases = await GetDatabases();
        databases.Should().NotContain(db);
    }

    [Fact]
    public async Task Does_not_error_if_configured_to_create_but_already_exists()
    {
        var db = "DAATAA";

        // Create the database manually before running the migration
        await CreateDatabaseFromConnectionString(db, Context.UserConnectionString(db));

        // Check that the database has been created
        IEnumerable<string> databasesBeforeMigration = await GetDatabases();
        databasesBeforeMigration.Should().Contain(db);

        var config = GetConfiguration(true, Context.UserConnectionString(db), Context.AdminConnectionString);
        await using var migrator = GetMigrator(config);

        // There should be no errors running the migration
        Exception? ex = await Record.ExceptionAsync(async () => await migrator.Migrate());
        ex.Should().BeNull();
        
    }

    [Theory]
    [InlineData("Invalid stuff")]
    [InlineData(null)]
    public async Task Does_not_need_admin_connection_if_database_already_exists(string? adminConnectionString)
    {
        var db = "DATADATBADATABASE";

        // Create the database manually before running the migration
        await CreateDatabaseFromConnectionString(db, Context.UserConnectionString(db));

        // Check that the database has been created
        IEnumerable<string> databasesBeforeMigration = await GetDatabases();
        databasesBeforeMigration.Should().Contain(db);
        

        // Change the admin connection string to rubbish and run the migration
        var config = GetConfiguration(true, Context.UserConnectionString(db), adminConnectionString);
        await using var migrator = GetMigrator(config);

        Exception? ex = await Record.ExceptionAsync(async () => await migrator.Migrate());
        ex.Should().BeNull();
    }


    protected Task CreateDatabase(string db) => CreateDatabaseFromConnectionString(db, Context.ConnectionString(db));

    protected virtual async Task CreateDatabaseFromConnectionString(string db, string connectionString)
    {
        var uid = TestConfig.Username(connectionString);
        var pwd = TestConfig.Password(connectionString);

        using (new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled))
        {
            for (var i = 0; i < 5; i++)
            {
                try
                {
                    using var conn = Context.CreateAdminDbConnection();

                    try
                    {
                        var commandText = Context.Syntax.CreateDatabase(db, pwd);
                        await conn.ExecuteAsync(commandText);
                    }
                    catch (DbException)
                    {
                    }
                    
                    try
                    {
                        var createUserSql = Context.Sql.CreateUser(db, uid, pwd);
                        if (createUserSql is not null)
                        {
                            await conn.ExecuteAsync(createUserSql);
                        }
                    }
                    catch (DbException)
                    {
                    }

                    var grantAccessSql = Context.Sql.GrantAccess(db, uid);
                    if (grantAccessSql is not null)
                    {
                        await conn.ExecuteAsync(grantAccessSql);
                    }

                    break;
                }
                catch (DbException)
                {
                }
                
            }
        }
    }

    protected virtual async Task<IEnumerable<string>> GetDatabases()
    {
        IEnumerable<string> databases = Enumerable.Empty<string>();
        string sql = Context.Syntax.ListDatabases;

        using (new TransactionScope(TransactionScopeOption.Suppress, TransactionScopeAsyncFlowOption.Enabled))
        {
            for (var i = 0; i < 5; i++)
            {
                try
                {
                    using var conn = Context.CreateAdminDbConnection();
                    databases = (await conn.QueryAsync<string>(sql)).ToArray();
                    break;
                }
                catch (DbException) { }
            }
        }
        return databases;
    }

    protected virtual bool ThrowOnMissingDatabase => true;


    protected IGrateMigrator GetMigrator(GrateConfiguration config) => Context.Migrator.WithConfiguration(config);

    protected GrateConfiguration GetConfiguration(string databaseName, bool createDatabase)
        => GetConfiguration(databaseName, createDatabase, Context.AdminConnectionString);


    private GrateConfiguration GetConfiguration(string databaseName, bool createDatabase, string? adminConnectionString)
        => GetConfiguration(createDatabase, Context.ConnectionString(databaseName), adminConnectionString);


    private GrateConfiguration GetConfiguration(bool createDatabase, string? connectionString, string? adminConnectionString)
    {
        var parent = TestConfig.CreateRandomTempDirectory();
        return new()
        {
            CreateDatabase = createDatabase,
            ConnectionString = connectionString,
            AdminConnectionString = adminConnectionString,
            Folders = Folders.Default,
            NonInteractive = true,
            SqlFilesDirectory = parent
        };
    }


    protected static DirectoryInfo Wrap(DirectoryInfo root, string? subFolder) =>
        new(Path.Combine(root.ToString(), subFolder ?? ""));

}
