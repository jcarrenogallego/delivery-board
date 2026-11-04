using Npgsql;

namespace DeliveryBoard.Infrastructure.Persistence;

/// <summary>Provisions the application-owned PostgreSQL reader after migrations.</summary>
public static class McpReaderProvisioner
{
    private const string RoleName = "mcp_reader";
    private const string OwnershipMarker = "DeliveryBoard MCP reader v1";

    public static async Task ProvisionAsync(
        string connectionString,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ExecuteAsync(connection, transaction,
            "SELECT pg_advisory_xact_lock(748195032)", cancellationToken);

        var exists = false;
        await using (var roleCommand = new NpgsqlCommand("""
            SELECT shobj_description(r.oid, 'pg_authid'),
                   r.rolsuper OR r.rolcreatedb OR r.rolcreaterole OR r.rolreplication
                       OR r.rolbypassrls OR r.rolinherit,
                   EXISTS (SELECT 1 FROM pg_auth_members m WHERE m.member = r.oid),
                   EXISTS (SELECT 1 FROM pg_shdepend d
                           WHERE d.refclassid = 'pg_authid'::regclass
                             AND d.refobjid = r.oid AND d.deptype = 'o')
            FROM pg_roles r WHERE r.rolname = 'mcp_reader'
            """, connection, transaction))
        await using (var reader = await roleCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                exists = true;
                if (reader.IsDBNull(0) || reader.GetString(0) != OwnershipMarker
                    || reader.GetBoolean(1) || reader.GetBoolean(2) || reader.GetBoolean(3))
                {
                    throw new InvalidOperationException(
                        "The existing mcp_reader role is unmanaged or unsafe; provisioning was stopped without changing it.");
                }
            }
        }

        if (exists)
        {
            await EnsureReadOnlyPrivilegesAsync(connection, transaction, cancellationToken);
        }

        // PostgreSQL utility statements do not accept password parameters. A transaction-local
        // setting keeps the secret out of the command text and quotes it on the server instead.
        await using (var secretCommand = new NpgsqlCommand(
            "SELECT set_config('deliveryboard.mcp_reader_password', @password, true)", connection, transaction))
        {
            secretCommand.Parameters.AddWithValue("password", password);
            await secretCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var verb = exists ? "ALTER" : "CREATE";
        try
        {
            await ExecuteAsync(connection, transaction, $$"""
                DO $provision$
                BEGIN
                    EXECUTE format('{{verb}} ROLE mcp_reader LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS PASSWORD %L',
                                   current_setting('deliveryboard.mcp_reader_password'));
                END
                $provision$;
                """, cancellationToken);
        }
        catch (PostgresException)
        {
            // Dynamic SQL error context can contain the password; never propagate it.
            throw new InvalidOperationException("Could not configure the MCP reader credentials.");
        }

        await ExecuteAsync(connection, transaction, """
            COMMENT ON ROLE mcp_reader IS 'DeliveryBoard MCP reader v1';
            ALTER ROLE mcp_reader SET default_transaction_read_only = on;
            ALTER ROLE mcp_reader SET statement_timeout = '30s';
            ALTER ROLE mcp_reader SET lock_timeout = '5s';
            ALTER ROLE mcp_reader SET idle_in_transaction_session_timeout = '30s';
            """, cancellationToken);

        using var quoting = new NpgsqlCommandBuilder();
        await ExecuteAsync(connection, transaction,
            $"GRANT CONNECT ON DATABASE {quoting.QuoteIdentifier(connection.Database)} TO {RoleName}",
            cancellationToken);

        var schemas = new List<string>();
        await using (var schemaCommand = new NpgsqlCommand("""
            SELECT nspname FROM pg_namespace
            WHERE nspname <> 'information_schema' AND left(nspname, 3) <> 'pg_'
            ORDER BY nspname
            """, connection, transaction))
        await using (var reader = await schemaCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                schemas.Add(reader.GetString(0));
            }
        }

        foreach (var schema in schemas)
        {
            var identifier = quoting.QuoteIdentifier(schema);
            await ExecuteAsync(connection, transaction, $"""
                GRANT USAGE ON SCHEMA {identifier} TO mcp_reader;
                GRANT SELECT ON ALL TABLES IN SCHEMA {identifier} TO mcp_reader;
                ALTER DEFAULT PRIVILEGES IN SCHEMA {identifier} GRANT SELECT ON TABLES TO mcp_reader;
                """, cancellationToken);
        }

        await EnsureReadOnlyPrivilegesAsync(connection, transaction, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task EnsureReadOnlyPrivilegesAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            SELECT has_database_privilege('mcp_reader', current_database(), 'CREATE')
                OR EXISTS (
                    SELECT 1 FROM pg_namespace n
                    WHERE n.nspname <> 'information_schema' AND left(n.nspname, 3) <> 'pg_'
                      AND has_schema_privilege('mcp_reader', n.oid, 'CREATE'))
                OR EXISTS (
                    SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname <> 'information_schema' AND left(n.nspname, 3) <> 'pg_'
                      AND CASE WHEN c.relkind IN ('r', 'p', 'v', 'm', 'f')
                          THEN has_table_privilege('mcp_reader', c.oid,
                              'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                          ELSE false END)
                OR EXISTS (
                    SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                    WHERE n.nspname <> 'information_schema' AND left(n.nspname, 3) <> 'pg_'
                      AND CASE WHEN c.relkind = 'S'
                          THEN has_sequence_privilege('mcp_reader', c.oid, 'USAGE,UPDATE')
                          ELSE false END)
            """, connection, transaction);
        if (await command.ExecuteScalarAsync(cancellationToken) is true)
        {
            throw new InvalidOperationException(
                "The mcp_reader role has effective write privileges; provisioning was stopped. Review role and PUBLIC grants.");
        }
    }

    private static async Task ExecuteAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
