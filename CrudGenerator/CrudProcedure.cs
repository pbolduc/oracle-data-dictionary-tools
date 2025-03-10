using Microsoft.EntityFrameworkCore;
using Oracle.DataDictionary;
using System.Text;

namespace CrudGenerator;

public abstract class PrimaryKeyStrategy
{
}

/// <summary>
/// Use a sequence to generate the primary key
/// </summary>
public class SequencePrimaryKeyStrategy : PrimaryKeyStrategy
{
    public SequencePrimaryKeyStrategy(string sequence)
    {
        Sequence = sequence;
    }
    public string Sequence { get; }
}

/// <summary>
/// Use the value supplied
/// </summary>
public class ValuePrimaryKeyStrategy : PrimaryKeyStrategy
{
}

public class TableMetadata
{
    private readonly OracleDataDictionaryDbContext _context;
    private readonly IEnumerable<string> _auditColumns;

    public TableMetadata(OracleDataDictionaryDbContext context, string tableName, PrimaryKeyStrategy primaryKeyStrategy, IEnumerable<string> auditColumns)
    {
        _context = context;

        Table = _context.Tables
            .Where(table => table.Name == tableName)
            .Include(table => table.Columns)
            .Single();

        PrimaryKeyStrategy = primaryKeyStrategy;
        _auditColumns = auditColumns ?? Enumerable.Empty<string>();
    }


    public Table Table { get; }

    public PrimaryKeyStrategy PrimaryKeyStrategy { get; }

    public IEnumerable<string> AuditColumns => _auditColumns;

    public Constraint PrimaryKey => _context
        .PrimaryKeyFor(Table)
        .Include(primaryKey => primaryKey.Columns)
        .Single();

    public bool IsForeignKey(TableColumn column)
    {
        return _context
            .ForeignKeysFor(Table)
            .Any(fk => fk.Columns.Any(fkColumn => fkColumn.ColumnName == column.Name));
    }
}

public class ToJson
{
    public static string Generate(TableMetadata tableInfo)
    {
        var table = tableInfo.Table;
        var columns = table.Columns
            .Where(column => !tableInfo.AuditColumns.Contains(column.Name))
            .OrderBy(_ => _.ColumnId)
            .ToList();

        var buffer = new StringBuilder();
        buffer.AppendLine("-- --------------------------------------------------------------------------------");
        buffer.AppendLine($"-- Convert a row from the {tableInfo.Table.Name} table to a json_object_t.");
        buffer.AppendLine("-- --------------------------------------------------------------------------------");
        buffer.AppendLine($"FUNCTION to_json_object(p_row {table.Name.ToLower()}%rowtype) RETURN json_object_t IS");
        buffer.AppendLine("    l_json json_object_t;");
        buffer.AppendLine("BEGIN");
        buffer.AppendLine("    l_json := json_object_t();");
        foreach (var column in columns)
        {
            buffer.AppendLine($"    l_json.put('{column.Name.ToLower()}', p_row.{column.Name.ToLower()});");
        }
        buffer.AppendLine("    RETURN l_json;");
        buffer.AppendLine("END;");

        return buffer.ToString();
    }
}

public class CrudProcedure
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="tableName"></param>
    /// <param name="sequenceName"></param>
    /// <param name="skipColumns">The columns that are skipped on insert.</param>
    /// <returns></returns>
    public string Insert(TableMetadata tableInfo, bool specification = false)
    {
        var table = tableInfo.Table;

        List<TableColumn> columns = table.Columns
            .Where(_ => !tableInfo.AuditColumns.Contains(_.Name))
            .OrderBy(_ => _.ColumnId)
            .ToList();

        string direction = tableInfo.PrimaryKeyStrategy is SequencePrimaryKeyStrategy
            ? "in out"
            : "in";

        string primaryKeyColumn = tableInfo.PrimaryKey.Columns.Single().ColumnName;

        var buffer = new StringBuilder();

        buffer.AppendLine("-- --------------------------------------------------------------------------------");
        buffer.AppendLine($"-- Insert a row into the {tableInfo.Table.Name} table using its rowtype.");
        if (tableInfo.PrimaryKeyStrategy is SequencePrimaryKeyStrategy strategy)
        {
            buffer.AppendLine($"-- The primary key is generated using the sequence {strategy.Sequence}.");
            buffer.AppendLine($"-- The value will be populated and returned in field p_row.{primaryKeyColumn.ToLower()}.");
        }

        buffer.AppendLine("-- --------------------------------------------------------------------------------");


        buffer.Append($"PROCEDURE insert_row(p_row {direction} {table.Name.ToLower()}%rowtype)");

        if (specification)
        {
            buffer.AppendLine(";");
            return buffer.ToString();
        }

        buffer.AppendLine(" IS");
        buffer.AppendLine("BEGIN");
        buffer.AppendLine($"    INSERT INTO {table.Name.ToLower()}");
        buffer.AppendLine("    (");

        for (var i = 0; i < columns.Count; i++)
        {
            buffer.Append($"        {columns[i].Name.ToLower()}");
            buffer.AppendLine(i < columns.Count - 1 ? "," : string.Empty);
        }
        buffer.AppendLine("    ) VALUES (");

        if (tableInfo.PrimaryKeyStrategy is SequencePrimaryKeyStrategy primaryKeyStrategy)
        {
            buffer.AppendLine($"        {primaryKeyStrategy.Sequence}.nextval,");
        }
        else if (tableInfo.PrimaryKeyStrategy is ValuePrimaryKeyStrategy)
        {
            buffer.Append($"        p_row.{primaryKeyColumn.ToLower()},");
        }

        for (var i = 0; i < columns.Count; i++)
        {
            if (primaryKeyColumn != columns[i].Name)
            {
                buffer.Append($"        p_row.{columns[i].Name.ToLower()}");
                buffer.AppendLine(i < columns.Count - 1 ? "," : string.Empty);
            }
        }
        if (tableInfo.PrimaryKeyStrategy is SequencePrimaryKeyStrategy)
        {
            buffer.AppendLine("    )");
            buffer.AppendLine($"    returning {primaryKeyColumn.ToLower()} into p_row.{primaryKeyColumn.ToLower()};");
        }
        else
        {
            buffer.AppendLine("    );");

        }
        buffer.AppendLine("END;");
        return buffer.ToString();
    }

    public string JsonToRowType(TableMetadata tableInfo, bool specification = false)
    {
        string tableName = tableInfo.Table.Name;
        var skipColumns = tableInfo.AuditColumns;

        StringBuilder buffer = new StringBuilder();

        buffer.AppendLine("-- --------------------------------------------------------------------------------");
        buffer.AppendLine($"-- Extract the fields from a json_object_t into record of type {tableInfo.Table.Name}");
        buffer.AppendLine("-- Only the fields that exist in the JSON object will be written to the record.");
        buffer.AppendLine("-- Fields not in the JSON object will not be modified.");
        buffer.AppendLine("-- --------------------------------------------------------------------------------");
        buffer.AppendLine("PROCEDURE json_object_to_rowtype(p_json json_object_t,");
        buffer.Append(   $"                                 p_row in out {tableName.ToLower()}%rowtype)");

        if (specification)
        {
            buffer.AppendLine(";");
            return buffer.ToString();
        }

        buffer.AppendLine(" IS");
        buffer.AppendLine("BEGIN");

        CopyColumns(buffer, tableInfo, "p_row", "p_json");

        buffer.AppendLine("END;");

        return buffer.ToString();
    }

    public string Update(TableMetadata tableInfo, bool specification = false)
    {
        var table = tableInfo.Table;
        string tableName = tableInfo.Table.Name;
        var skipColumns = tableInfo.AuditColumns;

        List<TableColumn> columns = table.Columns
            .Where(_ => !skipColumns.Contains(_.Name))
            .OrderBy(_ => _.ColumnId)
            .ToList();

        Constraint? pk = tableInfo.PrimaryKey;

        string primaryKeyColumn = pk.Columns.Single().ColumnName;

        var buffer = new StringBuilder();
        buffer.AppendLine("-- --------------------------------------------------------------------------------");
        buffer.AppendLine($"-- Update a row in the {tableInfo.Table.Name} table using its rowtype.");
        buffer.AppendLine("-- --------------------------------------------------------------------------------");
        buffer.Append($"PROCEDURE update_row(p_row in {tableName.ToLower()}%rowtype)");

        if (specification)
        {
            buffer.AppendLine(";");
            return buffer.ToString();
        }

        buffer.AppendLine(" IS");

        buffer.AppendLine("BEGIN");
        buffer.AppendLine($"    UPDATE {tableName.ToLower()}");
        buffer.Append("       SET");
        bool first = true;

        var maxLength = columns
            .Where(column => column.Name != primaryKeyColumn)
            .Max(column => column.Name.Length);

        for (var i = 0; i < columns.Count; i++)
        {
            if (primaryKeyColumn != columns[i].Name)
            {
                string padding = " ";
                if (first)
                {
                    first = false;
                }
                else
                {
                    padding = "           ";
                }

                buffer.Append($"{padding}{columns[i].Name.ToLower()} ");
                if (columns[i].Name.Length < maxLength)
                {
                    buffer.Append(new string(' ', maxLength - columns[i].Name.Length));
                }
                buffer.Append($"= p_row.{columns[i].Name.ToLower()}");
                buffer.AppendLine(i < columns.Count - 1 ? "," : string.Empty);
            }
        }

        buffer.AppendLine($"     WHERE {primaryKeyColumn.ToLower()} = p_row.{primaryKeyColumn.ToLower()};");
        buffer.AppendLine("END;");
        return buffer.ToString();
    }

    public string Delete(TableMetadata tableInfo, bool specification = false)
    {
        string tableName = tableInfo.Table.Name;
        string primaryKeyColumn = tableInfo.PrimaryKey.Columns.Single().ColumnName;

        var buffer = new StringBuilder();
        buffer.AppendLine("-- --------------------------------------------------------------------------------");
        buffer.AppendLine($"-- Delete a row from the {tableInfo.Table.Name} table using its rowtype.");
        buffer.AppendLine($"-- Only the primary key columns are used in the WHERE clause.");
        buffer.AppendLine("-- --------------------------------------------------------------------------------");

        buffer.Append($"PROCEDURE delete_row(p_row in {tableName.ToLower()}%rowtype)");

        if (specification)
        {
            buffer.AppendLine(";");
            return buffer.ToString();
        }

        buffer.AppendLine(" IS");
        buffer.AppendLine("BEGIN");
        buffer.AppendLine($"    DELETE");
        buffer.AppendLine($"      FROM {tableName.ToLower()}");
        buffer.AppendLine($"     WHERE {primaryKeyColumn.ToLower()} = p_row.{primaryKeyColumn.ToLower()};");
        buffer.AppendLine("END;");
        return buffer.ToString();
    }

    private void CopyColumns(StringBuilder buffer, TableMetadata tableInfo, string recordVariableName, string jsonVariableName)
    {
        Constraint? pk = tableInfo.PrimaryKey;

        var columns = tableInfo.Table.Columns
            .OrderBy(column => column.ColumnId)
            .ToList();

        var skipColumns = tableInfo.AuditColumns;

        foreach (var column in columns)
        {
            if (!skipColumns.Contains(column.Name))
            {
                var name = column.Name.ToLower();
                string function = "get_string";
                if (column.DataType == "NUMBER")
                {
                    function = "get_number";
                }
                else if (column.DataType == "DATE")
                {
                    function = "get_date";
                }

                buffer.AppendLine($"    if {jsonVariableName}.has('{name}') then");
                buffer.AppendLine($"        {recordVariableName}.{name} := {jsonVariableName}.{function}('{name}');");
                buffer.AppendLine($"    end if;");
            }
        }
    }
}
