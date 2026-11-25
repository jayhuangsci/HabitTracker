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
    public Guid[] ReferenceIds { get; init; } = [];
    public SearchQuery? Query { get; init; }
    public JsonElement Body { get; init; }
}

public static class ToolRunner
{
    public static async Task<object> Execute(CrewClient api, ToolRequest request)
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
            "session" => await api.Session(),
            "list_projects" => await api.Projects(),
            "get_project" => await api.Send<ProjectDto>(HttpMethod.Get, $"api/projects/{Project()}"),
            "create_project" => await api.SaveProject(null, Body<ProjectInput>()),
            "update_project" => await api.SaveProject(Project(), Body<ProjectInput>()),
            "list_employees" => await api.Employees(Project()),
            "get_employee" => await api.Send<EmployeeDto>(HttpMethod.Get, $"api/projects/{Project()}/employees/{Employee()}"),
            "create_employee" => await api.SaveEmployee(Project(), null, Body<EmployeeInput>()),
            "update_employee" => await api.SaveEmployee(Project(), Employee(), Body<EmployeeInput>()),
            "list_templates" => await api.Templates(),
            "list_chains" => await api.Chains(Project()),
            "get_chain" => await api.Send<ChainDto>(HttpMethod.Get, $"api/projects/{Project()}/chains/{Chain()}"),
            "create_chain" => await api.SaveChain(Project(), null, Body<ChainInput>()),
            "update_chain" => await api.SaveChain(Project(), Chain(), Body<ChainInput>()),
            "save_round" => await api.SaveRound(Project(), Chain(), Body<RoundInput>()),
            "save_result" => await api.SaveResult(Project(), Round(), Body<ResultInput>()),
            "get_round" => await api.Round(Project(), Round()),
            "get_by_request" => await api.ByRequest(Project(), Id(request.ClientRequestId, "clientRequestId")),
            "get_context" => await api.Context(Project(), Chain(), Employee(), request.RoundId, request.ReferenceIds),
            "search_rounds" => await api.Search(Project(), request.Query ?? new()),
            "get_graph" => await api.Graph(Project(), Chain()),
            "compare_rounds" => await api.Compare(Project(), Round(), Id(request.OtherRoundId, "otherRoundId"), request.LeftVersion, request.RightVersion),
            _ => throw new ArgumentException("Unknown operation. See tools/promptcrew.function.json.")
        };
    }
}
