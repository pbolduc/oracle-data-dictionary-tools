// See https://aka.ms/new-console-template for more information
using CrudGenerator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Oracle.DataDictionary;
using Oracle.ManagedDataAccess.Client;
using System.Text;

internal class Program
{
    private static OracleDataDictionaryDbContext CreateDbContext(string dataSource, string username, string password)
    {
        username = username.ToLower();

        var optionsBuilder = new DbContextOptionsBuilder<OracleDataDictionaryDbContext>();
        optionsBuilder.UseOracle($"Data Source={dataSource};User ID={username};Password={password};");

        var dbContext = new OracleDataDictionaryDbContext(optionsBuilder.Options);
        return dbContext;
    }

    private static AppConfiguration GetConfiguration()
    {
        // Build configuration
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddUserSecrets<Program>();

        var appConfig = new AppConfiguration();
        IConfiguration configuration = builder.Build();
        configuration.GetSection("AppConfiguration").Bind(appConfig);

        return appConfig;
    }

    private static void Main(string[] args)
    {
        List<string> auditColumns = new List<string>
        {
            "ENT_DTM",
            "ENT_USER_ID",
            "UPD_DTM",
            "UPD_USER_ID"
        };

        var configuration = GetConfiguration();
        OracleConfiguration.OracleDataSources.Add(configuration.sid, $"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={configuration.host})(PORT=1521))(CONNECT_DATA=(SID={configuration.sid})(SERVER=dedicated)))");

#if false
OracleDataDictionaryDbContext context = CreateDbContext(configuration.sid, configuration.occam.username, configuration.occam.password);

List<TableMetadata> tables =
[
    new TableMetadata(context, "OCCAM_AUDIT_LOG_ENTRIES", new SequencePrimaryKeyStrategy("occam_alen_seq"), auditColumns),
    new TableMetadata(context, "OCCAM_DISPUTE_COUNTS", new SequencePrimaryKeyStrategy("occam_dico_seq"), auditColumns),
    new TableMetadata(context, "OCCAM_DISPUTE_UPDATE_REQUESTS", new SequencePrimaryKeyStrategy("occam_dure_seq"), auditColumns),
    new TableMetadata(context, "OCCAM_DISPUTES", new SequencePrimaryKeyStrategy("occam_disp_seq"), auditColumns),
    new TableMetadata(context, "OCCAM_ERROR_LOGS", new SequencePrimaryKeyStrategy("occam_erlo_seq"), auditColumns),
    new TableMetadata(context, "OCCAM_OUTGOING_EMAILS", new SequencePrimaryKeyStrategy("occam_ouem_seq"), auditColumns),
    new TableMetadata(context, "OCCAM_VIOLATION_TICKET_COUNTS", new SequencePrimaryKeyStrategy("occam_vitc_seq"), auditColumns),
    new TableMetadata(context, "OCCAM_VIOLATION_TICKET_UPLOADS", new SequencePrimaryKeyStrategy("occam_vitu_seq"), auditColumns),
];

#else

        OracleDataDictionaryDbContext context = CreateDbContext(configuration.sid, configuration.tco.username, configuration.tco.password);

        List<TableMetadata> tables =
        [
            new TableMetadata(context, "TCO_APPEARANCE_CHARGE_COUNTS", new SequencePrimaryKeyStrategy("tco_apcc_seq"), auditColumns),
            new TableMetadata(context, "TCO_AUDIT_LOG_ENTRIES", new SequencePrimaryKeyStrategy("tco_ale_seq"), auditColumns),
            new TableMetadata(context, "TCO_COURT_APPEARANCES", new SequencePrimaryKeyStrategy("tco_coap_seq"), auditColumns),
            new TableMetadata(context, "TCO_DISPUTE_COUNTS", new SequencePrimaryKeyStrategy("tco_dico_seq"), auditColumns),
            new TableMetadata(context, "TCO_DISPUTE_REMARKS", new SequencePrimaryKeyStrategy("tco_dire_seq"), auditColumns),
            new TableMetadata(context, "TCO_DISPUTES", new SequencePrimaryKeyStrategy("tco_disp_seq"), auditColumns),
            new TableMetadata(context, "TCO_ERROR_LOGS", new SequencePrimaryKeyStrategy("tco_erlo_seq"), auditColumns),
            //new TableMetadata(context, "TCO_JJ_TEAM_AGENCY_XREF", new SequencePrimaryKeyStrategy("tco_jjat_seq"), auditColumns),
        ];
#endif

        // sort the tables by name
        tables.Sort((a, b) => string.Compare(a.Table.Name, b.Table.Name));

        string packageName = $"{tables.First().Table.Owner.ToLower()}_table_interface";

        StringBuilder buffer = new StringBuilder();

        CrudProcedure crud = new CrudProcedure();

        var now = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

        buffer.AppendLine($"CREATE OR REPLACE PACKAGE {packageName}");
        buffer.AppendLine("AS");
        buffer.AppendLine();
        buffer.AppendLine("-- This package contains procedures for creating, updating and deleting");
        buffer.AppendLine($"-- rows from the {tables.First().Table.Owner.ToLower()} schema.");
        buffer.AppendLine($"-- Date: {now}");
        buffer.AppendLine();
        foreach (var table in tables)
        {
            buffer.AppendLine("-- ================================================================================");
            buffer.AppendLine($"-- Operations on the {table.Table.Name} table");
            buffer.AppendLine("-- ================================================================================");
            buffer.AppendLine();
            buffer.AppendLine($"{crud.Insert(table, specification: true)}");
            buffer.AppendLine($"{crud.Update(table, specification: true)}");
            buffer.AppendLine($"{crud.Delete(table, specification: true)}");
            buffer.AppendLine($"{crud.JsonToRowType(table, specification: true)}");
        }

        buffer.AppendLine("END;");
        buffer.AppendLine("/");
        buffer.AppendLine();
        buffer.AppendLine($"CREATE OR REPLACE PACKAGE BODY {packageName}");
        buffer.AppendLine("AS");
        buffer.AppendLine();
        buffer.AppendLine("-- This package contains procedures for creating, updating and deleting");
        buffer.AppendLine($"-- rows from the {tables.First().Table.Owner.ToLower()} schema.");
        buffer.AppendLine($"-- Date: {now}");
        buffer.AppendLine();

        foreach (var table in tables)
        {
            buffer.AppendLine("-- ================================================================================");
            buffer.AppendLine($"-- Operations on the {table.Table.Name} table");
            buffer.AppendLine("-- ================================================================================");
            buffer.AppendLine();
            buffer.AppendLine(crud.Insert(table));
            buffer.AppendLine(crud.Update(table));
            buffer.AppendLine(crud.Delete(table));

            buffer.AppendLine(crud.JsonToRowType(table));
        }

        buffer.AppendLine();
        buffer.AppendLine("END;");

        Console.ReadLine();
    }
}

public class AppConfiguration
{
    public string host { get; set; }
    public string sid { get; set; }
    public UsernamePassword occam { get; set; }
    public UsernamePassword tco { get; set; }
}

public class UsernamePassword
{
    public string username { get; set; }
    public string password { get; set; }
}
