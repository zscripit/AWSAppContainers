"""Cliente de prueba para el socket TCP (puerto 6061). Solo usa la librería estándar.

Uso:
  python tcp_client.py 77.113.43.242 "{get:1}" "{insert:{\"nombre\":\"Leche\",\"precio\":24.5,\"categoriaId\":1}}"
  python tcp_client.py 77.113.43.242          -> modo interactivo (escribe mensajes, Enter para enviar, vacío para salir)
"""
import socket
import sys

PUERTO = 6061


def main():
    servidor = sys.argv[1] if len(sys.argv) > 1 else "localhost"
    mensajes = sys.argv[2:]

    with socket.create_connection((servidor, PUERTO), timeout=10) as s:
        respuestas = s.makefile("r", encoding="utf-8")

        def enviar(msg):
            print(f"> {msg}")
            s.sendall((msg + "\n").encode("utf-8"))
            print(f"< {respuestas.readline().strip()}")

        if mensajes:
            for m in mensajes:
                enviar(m)
        else:
            print(f"Conectado a {servidor}:{PUERTO}. Escribe un mensaje (vacío para salir).")
            while msg := input("> ").strip():
                s.sendall((msg + "\n").encode("utf-8"))
                print(f"< {respuestas.readline().strip()}")


if __name__ == "__main__":
    main()
