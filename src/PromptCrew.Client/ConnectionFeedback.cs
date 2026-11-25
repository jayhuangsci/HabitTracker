using System.Net;

namespace PromptCrew.Client;

public static class ConnectionFeedback
{
    // Fixed messages deliberately avoid URLs, response bodies, paths, and credentials.
    public static string Describe(Exception error, bool cancelled = false) => error switch
    {
        OperationCanceledException when cancelled => "操作已取消。草稿和原请求 ID 已保留，可检查保存状态后重试。",
        OperationCanceledException => "连接超时。请检查地址或网络；草稿和原请求 ID 已保留。",
        CrewApiException { Status: 401 } => "认证失败。请重新选择有效的 API Key 文件。",
        CrewApiException { Status: 403 } => "此密钥无权访问该项目。请使用具有项目权限的密钥。",
        CrewApiException { Status: 409 } => "记录或员工定义已改变。请检查保存状态，刷新后审阅冲突；原输入仍保留。",
        CrewApiException { Status: 404 } => "未找到项目、任务或记录。请刷新并检查当前项目。",
        CrewApiException { Status: 502 } => "地址可达，但返回内容不是 PromptCrew 数据。请检查 API 根地址。",
        CrewApiException { Status: >= 500 } => "服务暂时不可用。请稍后检查保存状态并重试。",
        HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError } => "无法找到服务地址。请检查域名和网络连接。",
        HttpRequestException => "无法连接服务。请确认本地服务已启动、地址正确且网络可用。",
        IOException or UnauthorizedAccessException => "无法读取密钥或写入草稿文件。请检查文件路径和访问权限。",
        _ => error.Message
    };
}
