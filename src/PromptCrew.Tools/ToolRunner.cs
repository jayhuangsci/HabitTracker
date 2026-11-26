using System.Text.Json;
using PromptCrew.Client;
using PromptCrew.Core;

namespace PromptCrew.Tools;

public sealed record ToolRequest
{
    public string Operation { get; init; } = "";
    public Guid? ProjectId { get; init; }
    public Guid? ChainId { get; init; }
    public Guid? EmployeeId { get; init; }
    public Guid? RoundId { get; init; }
    public Guid? OtherRoundId { get; init; }
    public int? LeftVersion { get; init; }
    public int? RightVersion { get; init; }
    public Guid? ClientRequestId { get; init; }
    public Guid? BeforeId { get; init; }
    public Guid[] ReferenceIds { get; init; } = [];
    public SearchQuery? Query { get; init; }
    public JsonElement Body { get; init; }
}

public static class ToolRunner
{
    public static async Task<object> Execute(CrewClient api, ToolRequest request, CancellationToken ct = default)
    {
        Guid Id(Guid? id, string field) => id.HasValue && id != Guid.Empty ? id.Value : throw new ArgumentException(field + " is required.");
        Guid Project() => Id(request.ProjectId, "projectId");
        Guid Chain() => Id(request.ChainId, "chainId");
        Guid Employee() => Id(request.EmployeeId, "employeeId");
        Guid Round() => Id(request.RoundId, "roundId");
        T Body<T>() => request.Body.ValueKind == JsonValueKind.Object
            ? request.Body.Deserialize<T>(CrewJson.Options) ?? throw new ArgumentException("body is required.")
            : throw new ArgumentException("body must be a JSON object.");
        return request.Operation switch
        {
            "session" => await api.Session(ct: ct),
            "list_projects" => await api.Projects(ct: ct),
            "get_project" => await api.Send<ProjectDto>(HttpMethod.Get, $"api/projects/{Project()}", ct: ct),
            "create_project" => await api.SaveProject(null, Body<ProjectInput>(), ct: ct),
            "update_project" => await api.SaveProject(Project(), Body<ProjectInput>(), ct: ct),
            "list_employees" => await api.Employees(Project(), ct: ct),
            "get_employee" => await api.Send<EmployeeDto>(HttpMethod.Get, $"api/projects/{Project()}/employees/{Employee()}", ct: ct),
            "create_employee" => await api.SaveEmployee(Project(), null, Body<EmployeeInput>(), ct: ct),
            "update_employee" => await api.SaveEmployee(Project(), Employee(), Body<EmployeeInput>(), ct: ct),
            "list_templates" => await api.Templates(ct: ct),
            "list_chains" => await api.Chains(Project(), ct, request.EmployeeId),
            "get_chain" => await api.Send<ChainDto>(HttpMethod.Get, $"api/projects/{Project()}/chains/{Chain()}", ct: ct),
            "create_chain" => await api.SaveChain(Project(), null, Body<ChainInput>(), ct: ct),
            "update_chain" => await api.SaveChain(Project(), Chain(), Body<ChainInput>(), ct: ct),
            "save_round" => await api.SaveRound(Project(), Chain(), Body<RoundInput>(), ct: ct),
            "save_result" => await api.SaveResult(Project(), Round(), Body<ResultInput>(), ct: ct),
            "get_round" => await api.Round(Project(), Round(), ct: ct),
            "get_by_request" => await api.ByRequest(Project(), Id(request.ClientRequestId, "clientRequestId"), ct: ct),
            "get_context" => await api.Context(Project(), Chain(), Employee(), request.RoundId, request.ReferenceIds, ct: ct),
            "search_rounds" => await api.Search(Project(), request.Query ?? new(), ct: ct),
            "list_activity" => await api.ActivityPage(Project(), Employee(), request.Query?.Skip ?? 0, request.Query?.Take ?? 50, ct, request.BeforeId),
            "get_graph" => await api.Graph(Project(), Chain(), ct: ct),
            "compare_rounds" => await api.Compare(Project(), Round(), Id(request.OtherRoundId, "otherRoundId"), request.LeftVersion, request.RightVersion, ct: ct),
            _ => throw new ArgumentException("Unknown operation. See tools/promptcrew.function.json.")
        };
    }
}
