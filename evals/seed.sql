BEGIN;
DO $guard$
BEGIN
  IF current_database() <> 'deliveryboard_eval' THEN
    RAISE EXCEPTION 'Solo se permite sembrar deliveryboard_eval';
  END IF;
  IF EXISTS (SELECT 1 FROM public.work_items) THEN
    RAISE EXCEPTION 'La base no esta vacia. No se borraran datos existentes';
  END IF;
END;
$guard$;

INSERT INTO public.work_items
  ("Id", "Title", "Owner", "Priority", "Status", "CreatedAt", "PrerequisiteIds", "Comment")
VALUES
  ('00000000-0000-4000-8000-000000000001', 'Laboratorio A', 'EvalSesion', 'Medium', 'Pending', '2026-01-01T00:00:00Z', ARRAY[]::uuid[], NULL),
  ('00000000-0000-4000-8000-000000000002', 'Laboratorio B', 'EvalSesion', 'Medium', 'Pending', '2026-01-01T00:00:00Z', ARRAY[]::uuid[], NULL),
  ('00000000-0000-4000-8000-000000000003', 'Laboratorio C', 'EvalSesion', 'High', 'Pending', '2026-01-01T00:00:00Z', ARRAY[]::uuid[], NULL);
COMMIT;
