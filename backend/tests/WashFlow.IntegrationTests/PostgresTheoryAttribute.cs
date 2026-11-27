namespace WashFlow.IntegrationTests;

public sealed class PostgresTheoryAttribute : TheoryAttribute
{
    public PostgresTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WASHFLOW_TEST_POSTGRES")))
            Skip = "WASHFLOW_TEST_POSTGRES yapılandırılmadı; gerçek PostgreSQL testi çalıştırılmadı.";
    }
}
