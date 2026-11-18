SELECT json_build_object(
 'collections', COALESCE((SELECT jsonb_agg(to_jsonb(c) ORDER BY c."Id") FROM "Collections" c), '[]'::jsonb),
 'receipts', COALESCE((SELECT jsonb_agg(to_jsonb(r) ORDER BY r."Id") FROM "Receipts" r), '[]'::jsonb),
 'periods', COALESCE((SELECT jsonb_agg(to_jsonb(p) ORDER BY p."Id") FROM "SubscriptionPeriods" p), '[]'::jsonb)
);
