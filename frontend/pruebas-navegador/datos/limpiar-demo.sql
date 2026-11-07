-- Borra la gente de demostracion (cascada desde usuarios).
DELETE FROM usuarios WHERE correo::text LIKE 'demo-%';
SELECT count(*) AS demo_restantes FROM usuarios WHERE correo::text LIKE 'demo-%';
