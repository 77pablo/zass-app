-- Borra TODO lo que sembrar_visual.sql creo. El cascade desde usuarios se lleva perfil,
-- suscripcion, coberturas, servicios y credenciales.
DELETE FROM usuarios WHERE correo::text LIKE 'visual-%';

SELECT
  (SELECT count(*) FROM usuarios WHERE correo::text LIKE 'visual-%')            AS usuarios_visual,
  (SELECT count(*) FROM perfiles_prestador WHERE slug LIKE 'visual-%')          AS perfiles_visual,
  (SELECT count(*) FROM perfiles_prestador WHERE estado = 'Publicado')          AS publicados_en_total;
