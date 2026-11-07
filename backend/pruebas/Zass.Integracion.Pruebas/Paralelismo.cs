// Las pruebas de integracion comparten una sola base. Cada una corre en su transaccion
// y la deshace, pero dos a la vez tocando el mismo indice unico se bloquearian entre si:
// una fila sin confirmar hace esperar a quien intenta insertar la misma clave.
// Correr en serie cuesta segundos y ahorra pruebas intermitentes, que son las peores.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
