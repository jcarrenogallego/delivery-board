using DeliveryBoard.Mcp.Application;

namespace DeliveryBoard.Application.UnitTests.Mcp;

public sealed class ReadQueryValidatorTests
{
    [Theory]
    [InlineData("SELECT 1")]
    [InlineData("select 1;")]
    [InlineData("/* outer /* nested */ comment */ SELECT 'delete; commit' AS \"update\"; -- end")]
    [InlineData("SELECT $$delete; commit$$, $tag$update; select$tag$")]
    [InlineData("SELECT E'escaped\\\'quote; delete', 'it''s; safe'")]
    [InlineData("WITH x AS (SELECT 1 AS value) SELECT value FROM x")]
    [InlineData("WITH RECURSIVE x AS (SELECT 1 UNION ALL SELECT 2) SELECT * FROM x")]
    [InlineData("SELECT count(*) FROM \"MixedCase\".\"Order\" WHERE \"Name\" = 'insert'")]
    public void AcceptsSingleReadStatementAndIgnoresQuotedTokens(string sql)
        => ReadQueryValidator.Validate(sql);

    [Theory]
    [InlineData("")]
    [InlineData(" -- comment only")]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("SELECT 1 -- comment\r;SELECT pg_advisory_lock(1)")]
    [InlineData("SELECT 1;;")]
    [InlineData("INSERT INTO fixture VALUES (1)")]
    [InlineData("UPDATE fixture SET id = 2")]
    [InlineData("DELETE FROM fixture")]
    [InlineData("DROP TABLE fixture")]
    [InlineData("TRUNCATE fixture")]
    [InlineData("COPY fixture TO STDOUT")]
    [InlineData("SET default_transaction_read_only = off")]
    [InlineData("COMMIT")]
    [InlineData("ROLLBACK")]
    [InlineData("BEGIN")]
    [InlineData("EXPLAIN ANALYZE DELETE FROM fixture")]
    [InlineData("WITH x AS (DELETE FROM fixture RETURNING *) SELECT * FROM x")]
    [InlineData("WITH x AS (INSERT INTO fixture VALUES (1) RETURNING *) SELECT * FROM x")]
    [InlineData("SELECT * INTO fixture FROM source")]
    [InlineData("SELECT * FROM fixture FOR UPDATE")]
    [InlineData("SELECT 'unterminated")]
    [InlineData("SELECT $$unterminated")]
    [InlineData("SELECT 1 /* unterminated")]
    public void RejectsWritesTransactionControlAndMalformedSql(string sql)
        => Assert.Throws<QueryRejectedException>(() => ReadQueryValidator.Validate(sql));
}
