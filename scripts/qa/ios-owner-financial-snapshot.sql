SELECT jsonb_build_object(
 'collections', COALESCE((SELECT jsonb_agg(to_jsonb(c) ORDER BY c."Id") FROM "Collections" c), '[]'::jsonb),
 'receipts', COALESCE((SELECT jsonb_agg(to_jsonb(r) ORDER BY r."Id") FROM "Receipts" r), '[]'::jsonb),
 'periods', COALESCE((SELECT jsonb_agg(to_jsonb(p) ORDER BY p."Id") FROM "SubscriptionPeriods" p), '[]'::jsonb),
 'players', COALESCE((SELECT jsonb_agg(to_jsonb(p) ORDER BY p."Id") FROM "Players" p), '[]'::jsonb),
 'enrollments', COALESCE((SELECT jsonb_agg(to_jsonb(e) ORDER BY e."Id") FROM "SportEnrollments" e), '[]'::jsonb),
 'receivableDocuments', COALESCE((SELECT jsonb_agg(to_jsonb(d) ORDER BY d."Id") FROM "ReceivableDocuments" d), '[]'::jsonb),
 'receivableMovements', COALESCE((SELECT jsonb_agg(to_jsonb(m) ORDER BY m."Id") FROM "ReceivableMovements" m), '[]'::jsonb),
 'receivableCreditApplications', COALESCE((SELECT jsonb_agg(to_jsonb(a) ORDER BY a."Id") FROM "ReceivableCreditApplications" a), '[]'::jsonb),
 'cashRefundPostings', COALESCE((SELECT jsonb_agg(to_jsonb(p) ORDER BY p."Id") FROM "CashRefundPostings" p), '[]'::jsonb),
 'cashSessions', COALESCE((SELECT jsonb_agg(to_jsonb(s) ORDER BY s."Id") FROM "CashSessions" s), '[]'::jsonb),
 'refunds', COALESCE((SELECT jsonb_agg(to_jsonb(r) ORDER BY r."Id") FROM "CollectionRefunds" r), '[]'::jsonb),
 'operatingExpenses', COALESCE((SELECT jsonb_agg(to_jsonb(e) ORDER BY e."Id") FROM "OperatingExpenses" e), '[]'::jsonb),
 'operatingExpenseAllocations', COALESCE((SELECT jsonb_agg(to_jsonb(a) ORDER BY a."Id") FROM "OperatingExpenseAllocations" a), '[]'::jsonb)
);
