import { useEffect, useRef } from "react";
import * as THREE from "three";

export default function DnaBackground() {
  const mountRef = useRef(null);

  useEffect(() => {
    const mount = mountRef.current;
    const renderer = new THREE.WebGLRenderer({ antialias: true });
    renderer.setClearColor(0x050f1a, 1);
    renderer.setSize(mount.clientWidth, mount.clientHeight);
    mount.appendChild(renderer.domElement);

    const scene = new THREE.Scene();
    const camera = new THREE.PerspectiveCamera(60, mount.clientWidth / mount.clientHeight, 0.1, 100);
    camera.position.set(0, 0, 18);

    const N = 28;
    const helixGroup = new THREE.Group();
    const matA = new THREE.MeshPhongMaterial({ color: 0x00d4ff, emissive: 0x0044aa, shininess: 120 });
    const matB = new THREE.MeshPhongMaterial({ color: 0xff4488, emissive: 0x880022, shininess: 120 });
    const matBond = new THREE.MeshPhongMaterial({ color: 0xaaffee, transparent: true, opacity: 0.5 });

    for (let i = 0; i < N; i++) {
      const t = (i / N) * Math.PI * 4;
      const r = 3;
      const y = (i / N) * 16 - 8;

      const sA = new THREE.Mesh(new THREE.SphereGeometry(0.22, 12, 12), matA);
      sA.position.set(Math.cos(t) * r, y, Math.sin(t) * r);
      helixGroup.add(sA);

      const sB = new THREE.Mesh(new THREE.SphereGeometry(0.22, 12, 12), matB);
      sB.position.set(Math.cos(t + Math.PI) * r, y, Math.sin(t + Math.PI) * r);
      helixGroup.add(sB);

      const makeStick = (from, to, color = 0x00d4ff) => {
        const dir = new THREE.Vector3().subVectors(to, from);
        const len = dir.length();
        const mid = new THREE.Vector3().addVectors(from, to).multiplyScalar(0.5);
        const cyl = new THREE.Mesh(
          new THREE.CylinderGeometry(0.04, 0.04, len, 6),
          new THREE.MeshPhongMaterial({ color, transparent: true, opacity: 0.3 })
        );
        cyl.position.copy(mid);
        cyl.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), dir.clone().normalize());
        helixGroup.add(cyl);
      };

      if (i < N - 1) {
        const t2 = ((i + 1) / N) * Math.PI * 4;
        const ny = ((i + 1) / N) * 16 - 8;
        makeStick(sA.position, new THREE.Vector3(Math.cos(t2) * r, ny, Math.sin(t2) * r));
        makeStick(sB.position, new THREE.Vector3(Math.cos(t2 + Math.PI) * r, ny, Math.sin(t2 + Math.PI) * r));
      }

      if (i % 3 === 0) {
        const dir = new THREE.Vector3().subVectors(sB.position, sA.position);
        const len = dir.length();
        const mid = new THREE.Vector3().addVectors(sA.position, sB.position).multiplyScalar(0.5);
        const bond = new THREE.Mesh(new THREE.CylinderGeometry(0.06, 0.06, len, 6), matBond);
        bond.position.copy(mid);
        bond.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), dir.clone().normalize());
        helixGroup.add(bond);
      }
    }
    scene.add(helixGroup);

    const particles = new THREE.Group();
    for (let i = 0; i < 60; i++) {
      const s = new THREE.Mesh(
        new THREE.SphereGeometry(0.06, 6, 6),
        new THREE.MeshBasicMaterial({ color: 0x00ffcc, transparent: true, opacity: 0.2 + Math.random() * 0.4 })
      );
      s.position.set((Math.random() - 0.5) * 30, (Math.random() - 0.5) * 20, (Math.random() - 0.5) * 10 - 5);
      s.userData.speed = (Math.random() - 0.5) * 0.01;
      particles.add(s);
    }
    scene.add(particles);

    scene.add(new THREE.AmbientLight(0x112233, 1.5));
    const pt1 = new THREE.PointLight(0x00d4ff, 2, 40);
    pt1.position.set(8, 5, 5);
    scene.add(pt1);
    const pt2 = new THREE.PointLight(0xff4488, 1.5, 40);
    pt2.position.set(-8, -5, 5);
    scene.add(pt2);

    let animId;
    let t = 0;
    const animate = () => {
      animId = requestAnimationFrame(animate);
      t += 0.008;
      helixGroup.rotation.y = t * 0.4;
      helixGroup.position.y = Math.sin(t * 0.3) * 0.3;
      pt1.position.x = Math.cos(t) * 10;
      pt1.position.z = Math.sin(t) * 8;
      particles.children.forEach((p) => {
        p.position.y += p.userData.speed;
        if (p.position.y > 10) p.position.y = -10;
        if (p.position.y < -10) p.position.y = 10;
      });
      renderer.render(scene, camera);
    };
    animate();

    const onResize = () => {
      camera.aspect = mount.clientWidth / mount.clientHeight;
      camera.updateProjectionMatrix();
      renderer.setSize(mount.clientWidth, mount.clientHeight);
    };
    window.addEventListener("resize", onResize);

    return () => {
      cancelAnimationFrame(animId);
      window.removeEventListener("resize", onResize);
      mount.removeChild(renderer.domElement);
      renderer.dispose();
    };
  }, []);

  return <div ref={mountRef} style={{ position: "fixed", inset: 0, zIndex: 0 }} />;
}
