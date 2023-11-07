
using Microsoft.EntityFrameworkCore;
using Oracle;
using Oracle.ManagedDataAccess.Client;

using Oracle.DataDictionary;

// using the Oracle Database 23c Free Developer Appliance virtual machine
OracleConfiguration.OracleDataSources.Add("free", "(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=freepdb1)(SERVER=dedicated)))");

var builder = new DbmlGenerationOptionsBuilder()
    .OutputSchemaName(true)
    .AddSchema("HR");

// add connections for all the sample schemas
foreach (var schema in new string[] { "HR", "OE", "PM", "IX", "BI", "AV", "SH" })
{
    builder.UseDbContext(schema, CreateDbContext("free", schema, "oracle"));
}

var options = builder.Build();

var generator = new OracleToDbml(options);
var dbml = generator.Generate();

Console.WriteLine(dbml);

OracleDataDictionaryDbContext CreateDbContext(string dataSource, string username, string password)
{
    username = username.ToLower();

    var optionsBuilder = new DbContextOptionsBuilder<OracleDataDictionaryDbContext>();
    optionsBuilder.UseOracle($"Data Source={dataSource};User ID={username};Password={password};");

    var dbContext = new OracleDataDictionaryDbContext(optionsBuilder.Options);
    return dbContext;
}
