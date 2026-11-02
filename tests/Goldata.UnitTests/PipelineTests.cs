using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Goldata.UnitTests;

/// <summary>HTTP pipeline checks that need no database: auth walls, CSRF and the MCP endpoint guard.</summary>
public class PipelineTests(PipelineTests.Factory factory) : IClassFixture<PipelineTests.Factory>
{
    public sealed class Factory : WebApplicationFactory<Program>
    {
        // Never pick up a developer's deploy/env/local.env in tests (Program loads it before the host is built).
        public Factory() => Environment.SetEnvironmentVariable("GOLDATA_ENV_FILE", Path.Combine(Path.GetTempPath(), "no-such-goldata.env"));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            // Unreachable DB: these tests must never touch it.
            builder.UseSetting("ConnectionStrings:Legacy", "Server=127.0.0.1;Port=1;Database=goldata;User ID=x;Password=x;Connect Timeout=1");
            builder.UseSetting("ConnectionStrings:App", "Server=127.0.0.1;Port=1;Database=goldata_app;User ID=x;Password=x;Connect Timeout=1");
            builder.UseSetting("Auth:TokenPepper", "test-pepper");
            builder.UseSetting("Documents:RootPath", Path.GetTempPath());
        }
    }

    private HttpClient Client() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Liveness_is_anonymous()
    {
        var res = await Client().GetAsync("/health/live", TestContext.Current.CancellationToken);
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.Headers.Contains("X-Request-Id").ShouldBeTrue();
    }

    [Theory]
    [InlineData("/api/v1/me")]
    [InlineData("/api/v1/modules")]
    [InlineData("/api/v1/modules/contracts")]
    [InlineData("/api/v1/files/1")]
    [InlineData("/api/v1/admin/users")]
    [InlineData("/api/v1/todos")]
    public async Task Api_requires_login(string path) =>
        (await Client().GetAsync(path, TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Unsafe_requests_without_antiforgery_token_are_rejected()
    {
        var res = await Client().PostAsync("/api/v1/auth/login",
            new StringContent("""{"login":"a","password":"b"}""", System.Text.Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);
        res.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("CSRF");
    }

    [Fact]
    public async Task Csrf_endpoint_issues_a_token()
    {
        var res = await Client().GetAsync("/api/v1/auth/csrf", TestContext.Current.CancellationToken);
        res.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await res.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("X-XSRF-TOKEN");
    }

    [Fact]
    public async Task Mcp_requires_a_bearer_token()
    {
        var res = await Client().PostAsync("/mcp", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);
        res.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Mcp_rejects_a_token_without_the_prefix()
    {
        var client = Client();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "not-a-goldata-token");
        var res = await client.PostAsync("/mcp", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);
        res.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
