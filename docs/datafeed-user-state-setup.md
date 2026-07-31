# DataFeed: user-state endpoints — setup instructions

What the frontend now expects from the DataFeed service: two routes that store and return one
JSON document per user per kind, backed by a DynamoDB table. The frontend half is already merged
(`UserStateSync` / `HttpUserStateStore`); until this lands, its saves fail quietly and retry.

## The contract (what the frontend calls)

Base: the existing API Gateway stage (`https://0ncy4qt6v1.execute-api.eu-north-1.amazonaws.com`),
behind the existing Cognito JWT authorizer — same as `/api/datasets`.

- `GET /api/user-state/{kind}`
  - 200 → the stored payload **verbatim** as the response body, `Content-Type: application/json`
  - 404 → the user has never saved this kind
  - 401/403 → handled by the authorizer as today
- `PUT /api/user-state/{kind}` with a JSON body
  - 204 on success
  - 400 → kind not in the allow-list, or body is not valid JSON
  - 413 → body over 256 KB

Rules:
- **kind allow-list**: `settings`, `dashboard`, `functions`, `workspace`, `network`. Reject
  anything else with 400 — this is what stops arbitrary keys landing in the table.
- **Identity comes from the token only.** `userSub` = the `sub` claim of the validated JWT
  (from the authorizer context / `HttpContext.User`). Never from a header, query, or body.
- **Size cap 256 KB** (DynamoDB's item limit is 400 KB; the margin covers attribute overhead).
- **The service does not parse the payload** beyond checking it is well-formed JSON. Shapes are
  the client's business (`schemaVersion` is inside the document).
- Concurrency: last-write-wins. Reserve `If-Match: <updatedAt>` for a later conditional write —
  accept and ignore the header for now.

## 1. Terraform — table + permissions (DataFeed repo's terraform)

```hcl
resource "aws_dynamodb_table" "user_state" {
  name         = "terrafa-user-state"
  billing_mode = "PAY_PER_REQUEST"
  hash_key     = "userSub"
  range_key    = "kind"

  attribute {
    name = "userSub"
    type = "S"
  }

  attribute {
    name = "kind"
    type = "S"
  }

  point_in_time_recovery {
    enabled = true
  }

  server_side_encryption {
    enabled = true
  }
}
```

Attach to the service's Lambda execution role (scoped to the table, no index ARNs needed):

```hcl
resource "aws_iam_role_policy" "user_state" {
  name = "user-state-rw"
  role = <the existing lambda execution role>.id

  policy = jsonencode({
    Version = "2012-10-17"
    Statement = [{
      Effect   = "Allow"
      Action   = ["dynamodb:GetItem", "dynamodb:PutItem"]
      Resource = aws_dynamodb_table.user_state.arn
    }]
  })
}
```

If the API Gateway routes are declared explicitly (rather than a `{proxy+}` catch-all), add
`GET` and `PUT` for `/api/user-state/{kind}` wired to the same Lambda integration and the same
Cognito authorizer as the dataset routes. With a proxy catch-all, nothing to do here.

Pass the table name to the service the same way its other config arrives (env var on the Lambda,
e.g. `USER_STATE_TABLE = aws_dynamodb_table.user_state.name`).

## 2. Service code (ASP.NET controller, matching the existing style)

NuGet: `AWSSDK.DynamoDBv2` (the low-level client is enough; no object mapper needed).

Register the client once alongside the existing AWS clients:

```csharp
services.AddSingleton<IAmazonDynamoDB>(_ => new AmazonDynamoDBClient());
```

Controller — RFC 7807 `Problem(...)` on every failure path, like the dataset controller:

```csharp
[ApiController]
[Route("api/user-state")]
public sealed class UserStateController(IAmazonDynamoDB dynamo) : ControllerBase
{
    private static readonly HashSet<string> Kinds =
        ["settings", "dashboard", "functions", "workspace", "network"];

    private const int MaxPayloadBytes = 256 * 1024;

    private static readonly string TableName =
        Environment.GetEnvironmentVariable("USER_STATE_TABLE") ?? "terrafa-user-state";

    /// <summary>The sub claim of the validated JWT — the only place identity comes from.</summary>
    private string? UserSub =>
        User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);

    [HttpGet("{kind}")]
    public async Task<IActionResult> Get(string kind, CancellationToken cancellationToken)
    {
        if (!Kinds.Contains(kind))
            return Problem(title: "Unknown kind", detail: $"'{kind}' is not a stored kind.", statusCode: 400);
        if (UserSub is not { Length: > 0 } sub)
            return Problem(title: "No subject", detail: "The token carries no sub claim.", statusCode: 401);

        var response = await dynamo.GetItemAsync(new GetItemRequest
        {
            TableName = TableName,
            Key = new Dictionary<string, AttributeValue>
            {
                ["userSub"] = new(sub),
                ["kind"] = new(kind)
            }
        }, cancellationToken);

        if (!response.IsItemSet || !response.Item.TryGetValue("payload", out var payload))
            return NotFound();

        return Content(payload.S, "application/json");
    }

    [HttpPut("{kind}")]
    public async Task<IActionResult> Put(string kind, CancellationToken cancellationToken)
    {
        if (!Kinds.Contains(kind))
            return Problem(title: "Unknown kind", detail: $"'{kind}' is not a stored kind.", statusCode: 400);
        if (UserSub is not { Length: > 0 } sub)
            return Problem(title: "No subject", detail: "The token carries no sub claim.", statusCode: 401);

        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(cancellationToken);

        if (Encoding.UTF8.GetByteCount(body) > MaxPayloadBytes)
            return Problem(title: "Payload too large", detail: "Documents are capped at 256 KB.", statusCode: 413);

        int schemaVersion;
        try
        {
            using var document = JsonDocument.Parse(body);
            schemaVersion = document.RootElement.TryGetProperty("schemaVersion", out var version)
                && version.ValueKind == JsonValueKind.Number
                    ? version.GetInt32()
                    : 0;
        }
        catch (JsonException)
        {
            return Problem(title: "Unreadable payload", detail: "The body is not valid JSON.", statusCode: 400);
        }

        await dynamo.PutItemAsync(new PutItemRequest
        {
            TableName = TableName,
            Item = new Dictionary<string, AttributeValue>
            {
                ["userSub"] = new(sub),
                ["kind"] = new(kind),
                ["payload"] = new(body),
                ["updatedAt"] = new(DateTime.UtcNow.ToString("o")),
                ["schemaVersion"] = new() { N = schemaVersion.ToString() }
            }
        }, cancellationToken);

        return NoContent();
    }
}
```

Notes:
- `schemaVersion` is mirrored to a top-level attribute purely for ops queries; the payload string
  is the source of truth.
- If the service enforces a global request-size limit smaller than 256 KB (Kestrel default is
  30 MB, API Gateway 10 MB — both fine), nothing to change.
- If the authorizer populates claims differently (e.g. `principalId` in the request context on a
  REST-API-type gateway), adjust `UserSub` to read from there — the invariant is only *token, not
  client input*.

## 3. Deploy order & verification

1. `terraform apply` (table + IAM + routes if explicit) in the DataFeed repo.
2. Deploy the service with the new controller and `USER_STATE_TABLE` set.
3. Smoke test with a real token (any signed-in user's access token):

```bash
TOKEN=<access token>
BASE=https://0ncy4qt6v1.execute-api.eu-north-1.amazonaws.com

# absent → 404
curl -i -H "Authorization: Bearer $TOKEN" $BASE/api/user-state/settings

# write → 204
curl -i -X PUT -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"schemaVersion":1,"uiScale":1.1}' $BASE/api/user-state/settings

# read back → 200 with the same body
curl -i -H "Authorization: Bearer $TOKEN" $BASE/api/user-state/settings

# unknown kind → 400; no token → 401 from the gateway
curl -i -X PUT -H "Authorization: Bearer $TOKEN" -d '{}' $BASE/api/user-state/nope
curl -i $BASE/api/user-state/settings
```

4. End-to-end from the app: sign in, move a dashboard tile or change a setting, wait ~2 s
   (the debounce), reload — the change should come back. Check the item in the console:
   `aws dynamodb get-item --table-name terrafa-user-state --key '{"userSub":{"S":"<sub>"},"kind":{"S":"dashboard"}}'`

Reminder: none of this fires until Cognito sign-in works end-to-end — the frontend only loads
and saves while signed in.
